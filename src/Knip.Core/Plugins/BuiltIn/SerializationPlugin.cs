using Knip.Core.Configuration;
using Knip.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Knip.Core.Plugins.BuiltIn;

/// <summary>
/// Keeps alive DTO data members that a JSON serializer reads/writes by reflecting over a type's
/// properties/fields — a member the walker never sees named in source (no <c>dto.Name</c> read), so it
/// is falsely flagged dead even when the DTO type itself is alive (it was passed to Serialize). Promotes H5.
///
/// Conservative (§3.8): roots ONLY the public data members of types that are DEMONSTRABLY serialized,
/// their collection element types, or a member EXPLICITLY serialization-annotated. It never blanket-roots
/// every property in the solution. A non-serialized DTO's plain members and unrelated collaborators stay
/// flagged (the over-rooting guard).
///
/// Built-in calls and attributes are matched by resolved namespace and defining assembly. Optional
/// <c>plugins.serialization.aliases</c> mappings preserve explicit support for source-only serializer
/// stand-ins and compatible user extensions. The optional <c>namespaces</c> glob remains the broad,
/// deliberate DTO escape hatch.
/// </summary>
internal sealed class SerializationPlugin : IKnipPlugin
{
    public const string NamespacesSettingKey = "namespaces";

    public string Id => "serialization";

    private const string SystemTextJsonSerializer = "System.Text.Json::System.Text.Json.JsonSerializer";
    private const string NewtonsoftJsonConvert = "Newtonsoft.Json::Newtonsoft.Json.JsonConvert";

    private static readonly string[] MemberAttributes =
    [
        "System.Text.Json::System.Text.Json.Serialization.JsonPropertyNameAttribute",
        "Newtonsoft.Json::Newtonsoft.Json.JsonPropertyAttribute",
        "System.Runtime.Serialization.Primitives::System.Runtime.Serialization.DataMemberAttribute",
        "System.Runtime.Serialization::System.Runtime.Serialization.DataMemberAttribute",
    ];

    public void Contribute(PluginContext ctx, CancellationToken ct)
    {
        var compilation = ctx.Compilation;
        var sink = ctx.Sink;
        var namespaceGlobs = ReadNamespaceGlobs(ctx.Settings);
        var matcher = new FrameworkTypeMatcher(ctx.Settings);

        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(ct);

            // (1) Serialize/deserialize calls: resolve the serialized target and root its own data members
            // plus data members of collection elements represented by that target.
            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method) continue;
                if (!IsSerializerMethod(method, matcher)) continue;

                foreach (var type in SerializedTypeTraversal.SelfAndCollectionElements(
                    SerializedType(model, inv, method, ct)))
                    RootDataMembers(type, sink);
            }

            // (2) Members explicitly annotated for serialization — root the member itself.
            foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                switch (member)
                {
                    case PropertyDeclarationSyntax prop
                        when model.GetDeclaredSymbol(prop, ct) is { } p && WearsMemberAttribute(p, matcher):
                        RootMember(p, sink);
                        break;
                    case FieldDeclarationSyntax field:
                        foreach (var v in field.Declaration.Variables)
                            if (model.GetDeclaredSymbol(v, ct) is IFieldSymbol f && WearsMemberAttribute(f, matcher))
                                RootMember(f, sink);
                        break;
                }
            }

            // (3) Optional: root data members of types whose namespace matches a configured glob.
            if (namespaceGlobs.Count > 0)
                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    if (model.GetDeclaredSymbol(typeDecl, ct) is INamedTypeSymbol type
                        && Glob.IsMatchAny(type.ContainingNamespace.ToDisplayString(), namespaceGlobs))
                        RootDataMembers(type, sink);
        }
    }

    /// <summary>
    /// The type a serialize/deserialize call targets: its explicit type argument if present
    /// (<c>Serialize&lt;T&gt;</c>/<c>Deserialize&lt;T&gt;</c>), else the static type of the value being
    /// serialized (the first argument of <c>Serialize(value)</c>). Conservative: null if neither resolves.
    /// </summary>
    private static ITypeSymbol? SerializedType(
        SemanticModel model, InvocationExpressionSyntax inv, IMethodSymbol method, CancellationToken ct)
    {
        // Prefer the generic type argument — Serialize<T>, Deserialize<T>, DeserializeObject<T>.
        if (method.TypeArguments.Length == 1 && method.TypeArguments[0] is { TypeKind: not TypeKind.Error } arg)
            return arg;

        // Otherwise the serialized VALUE's static type — Serialize(dto) / SerializeObject(dto). The first
        // argument that is a plain value (not a serializer-options / settings object) is the DTO.
        if (inv.ArgumentList.Arguments.Count > 0
            && model.GetTypeInfo(inv.ArgumentList.Arguments[0].Expression, ct).Type is { TypeKind: not TypeKind.Error } valueType)
            return valueType;

        return null;
    }

    /// <summary>
    /// Root a serialized type's public data members: get/set-able PROPERTIES and public FIELDS — the
    /// surface a JSON serializer reflects over. The type itself is already alive (it was referenced to be
    /// serialized); this keeps its otherwise-unread members alive. Over-rooting is scoped to THIS type's
    /// own data members (a false negative at worst, §3.8) — never its collaborators or methods.
    /// </summary>
    private static void RootDataMembers(ITypeSymbol type, IContributionSink sink)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared || member.IsStatic) continue;
            if (member.DeclaredAccessibility != Accessibility.Public) continue;

            switch (member)
            {
                // A public property a serializer would read/write: it must have a get and a set (indexers
                // are not serialized data members).
                case IPropertySymbol { GetMethod: not null, SetMethod: not null, IsIndexer: false } property:
                    RootMember(property, sink);
                    break;
                // A public instance field (const fields are compile-time, not serialized state).
                case IFieldSymbol { IsConst: false } field:
                    sink.AddRoot(field);
                    break;
            }
        }
    }

    /// <summary>Root a data member; for a property, its accessors too (both are serializer entry points).</summary>
    private static void RootMember(ISymbol member, IContributionSink sink)
    {
        sink.AddRoot(member);
        if (member is IPropertySymbol property)
        {
            if (property.GetMethod is { } getter) sink.AddRoot(getter);
            if (property.SetMethod is { } setter) sink.AddRoot(setter);
        }
    }

    private static bool IsSerializerMethod(IMethodSymbol method, FrameworkTypeMatcher matcher) =>
        method.Name switch
        {
            "Serialize" or "Deserialize" => matcher.Matches(method.ContainingType, SystemTextJsonSerializer),
            "SerializeObject" or "DeserializeObject" => matcher.Matches(method.ContainingType, NewtonsoftJsonConvert),
            _ => false,
        };

    private static bool WearsMemberAttribute(ISymbol member, FrameworkTypeMatcher matcher)
    {
        foreach (var attr in member.GetAttributes())
            if (MemberAttributes.Any(identity => matcher.MatchesAttribute(attr.AttributeClass, identity)))
                return true;

        return false;
    }

    /// <summary>Read the optional <c>namespaces</c> glob list from this plugin's settings block.</summary>
    private static IReadOnlyList<string> ReadNamespaceGlobs(PluginSettings settings)
    {
        if (!settings.Extra.TryGetValue(NamespacesSettingKey, out var element)
            || element.ValueKind != System.Text.Json.JsonValueKind.Array)
            return [];

        var globs = new List<string>();
        foreach (var item in element.EnumerateArray())
            if (item.ValueKind == System.Text.Json.JsonValueKind.String
                && item.GetString() is { Length: > 0 } glob)
                globs.Add(glob);
        return globs;
    }
}
