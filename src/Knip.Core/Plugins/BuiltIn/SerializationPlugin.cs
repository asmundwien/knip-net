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
/// Recognizes, matched by simple NAME (offline — no NuGet reference; fixtures use a local stand-in
/// serializer so the plugin ships with ZERO framework dependencies and is version-agnostic, invariant #9):
///   • A serialize/deserialize call whose target type <c>T</c> resolves — root public get/set PROPERTIES
///     and public FIELDS on T and its collection element types. T is taken from the invocation's type
///     argument (<c>Serialize&lt;T&gt;</c> / <c>Deserialize&lt;T&gt;</c> /
///     <c>DeserializeObject&lt;T&gt;</c>) or, failing that, from the type of the serialized argument
///     (<c>Serialize(dto)</c>). Method names matched: Serialize / Deserialize
///     (System.Text.Json.JsonSerializer), SerializeObject / DeserializeObject (Newtonsoft.Json.JsonConvert).
///   • A property/field wearing an attribute named <c>JsonPropertyName</c> / <c>JsonProperty</c> /
///     <c>DataMember</c> — a member explicitly marked for serialization — root that member.
///
/// OFF by default (opt-in via <c>plugins.serialization.enabled: true</c>): serialize/deserialize method
/// names are common enough that rooting every serialized type's members everywhere is not safe as a
/// default. When on, over-rooting here is a false negative at worst, scoped to a serialized type and its
/// collection elements, never unrelated collaborators.
///
/// Optional setting <c>plugins.serialization.namespaces</c> (glob list): also root the public data members
/// of types whose namespace matches — a project-wide "these are DTOs" escape hatch for serialization the
/// plugin cannot statically see (custom serializers, config binding). Off unless configured.
/// </summary>
internal sealed class SerializationPlugin : IKnipPlugin
{
    public const string NamespacesSettingKey = "namespaces";

    public string Id => "serialization";

    // Serialize/deserialize method simple names whose target type's data members are reflected over.
    private static readonly HashSet<string> SerializerMethodNames = new(StringComparer.Ordinal)
    {
        "Serialize",         // System.Text.Json.JsonSerializer.Serialize<T>
        "Deserialize",       // System.Text.Json.JsonSerializer.Deserialize<T>
        "SerializeObject",   // Newtonsoft.Json.JsonConvert.SerializeObject
        "DeserializeObject", // Newtonsoft.Json.JsonConvert.DeserializeObject<T>
    };

    // Member attribute simple names (with or without the "Attribute" suffix) that mark a member serialized.
    private static readonly HashSet<string> MemberAttributeNames = new(StringComparer.Ordinal)
    {
        "JsonPropertyName", // System.Text.Json.Serialization.JsonPropertyNameAttribute
        "JsonProperty",     // Newtonsoft.Json.JsonPropertyAttribute
        "DataMember",       // System.Runtime.Serialization.DataMemberAttribute
    };

    public void Contribute(PluginContext ctx, CancellationToken ct)
    {
        var compilation = ctx.Compilation;
        var sink = ctx.Sink;
        var namespaceGlobs = ReadNamespaceGlobs(ctx.Settings);

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
                if (!SerializerMethodNames.Contains(method.Name)) continue;

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
                        when model.GetDeclaredSymbol(prop, ct) is { } p && WearsMemberAttribute(p):
                        RootMember(p, sink);
                        break;
                    case FieldDeclarationSyntax field:
                        foreach (var v in field.Declaration.Variables)
                            if (model.GetDeclaredSymbol(v, ct) is IFieldSymbol f && WearsMemberAttribute(f))
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

    private static bool WearsMemberAttribute(ISymbol member)
    {
        foreach (var attr in member.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;
            if (name is null) continue;
            var trimmed = name.EndsWith("Attribute", StringComparison.Ordinal)
                ? name[..^"Attribute".Length]
                : name;
            if (MemberAttributeNames.Contains(trimmed)) return true;
        }
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
