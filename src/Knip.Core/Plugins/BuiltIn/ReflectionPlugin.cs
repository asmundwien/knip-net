using Knip.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Knip.Core.Plugins.BuiltIn;

/// <summary>
/// Keeps alive symbols that are only ever named by a reflection string/typeof, which the core walker
/// cannot see (a string literal is not an IdentifierName edge). Conservative (§3.8): only contributes
/// when it can RESOLVE the target symbol from the string/typeof — never blanket-roots. Promotes H1, H2.
///
/// Recognizes:
///   • Type.GetType("Ns.Foo") / assembly.GetType("Ns.Foo")           → roots the named TYPE (H2)
///   • Activator.CreateInstance(typeof(Foo)) / Activator.CreateInstance("Ns.Foo")
///                                                                      → roots the named/typeof'd TYPE
///   • …GetMethod("X") / GetProperty("X") / GetField("X") / GetEvent("X")
///        on a resolvable receiver type                                → roots the named MEMBER (H1)
/// </summary>
internal sealed class ReflectionPlugin : IKnipPlugin
{
    public string Id => "reflection";

    private static readonly string[] SystemTypeIdentities =
    [
        "System.Private.CoreLib::System.Type",
        "System.Runtime::System.Type",
        "mscorlib::System.Type",
    ];

    private static readonly string[] AssemblyIdentities =
    [
        "System.Private.CoreLib::System.Reflection.Assembly",
        "System.Runtime::System.Reflection.Assembly",
        "mscorlib::System.Reflection.Assembly",
    ];

    private static readonly string[] ActivatorIdentities =
    [
        "System.Private.CoreLib::System.Activator",
        "System.Runtime::System.Activator",
        "mscorlib::System.Activator",
    ];

    public void Contribute(PluginContext ctx, CancellationToken ct)
    {
        var compilation = ctx.Compilation;
        var sink = ctx.Sink;

        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(ct);

            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method) continue;
                if (IsMethod(method, SystemTypeIdentities, "GetType", SpecialType.System_String)
                    || IsMethod(method, AssemblyIdentities, "GetType", SpecialType.System_String))
                {
                    RootTypeFromStringArg(compilation, model, inv, sink, ct);
                }
                else if (IsTypeArgumentActivator(method))
                {
                    RootTypeFromTypeofArg(model, inv, sink, ct);
                }
                else if (IsMethod(
                    method,
                    ActivatorIdentities,
                    "CreateInstance",
                    SpecialType.System_String,
                    SpecialType.System_String))
                {
                    RootTypeFromStringArg(compilation, model, inv, sink, ct, argIndex: 1);
                }
                else if (SystemTypeIdentities.Any(identity =>
                             SymbolIdentity.MatchesType(method.ContainingType, identity))
                         && method.Name is "GetMethod" or "GetProperty" or "GetField" or "GetEvent")
                {
                    RootMemberFromString(model, inv, method.Name, sink, ct);
                }
            }
        }
    }

    private static bool IsMethod(
        IMethodSymbol method,
        string[] containingTypes,
        string name,
        params SpecialType[] parameterTypes) =>
        method.Name == name
        && containingTypes.Any(identity => SymbolIdentity.MatchesType(method.ContainingType, identity))
        && method.Parameters.Select(parameter => parameter.Type.SpecialType).SequenceEqual(parameterTypes);

    private static bool IsTypeArgumentActivator(IMethodSymbol method) =>
        method.Name == "CreateInstance"
        && ActivatorIdentities.Any(identity => SymbolIdentity.MatchesType(method.ContainingType, identity))
        && method.Parameters.Length == 1
        && SystemTypeIdentities.Any(identity =>
            SymbolIdentity.MatchesType(method.Parameters[0].Type as INamedTypeSymbol, identity));

    /// <summary>Resolve a string-literal type name argument to a type and root it (with its members).</summary>
    private static void RootTypeFromStringArg(
        Compilation compilation, SemanticModel model, InvocationExpressionSyntax inv,
        IContributionSink sink, CancellationToken ct, int argIndex = 0)
    {
        if (ConstStringArg(model, inv, argIndex, ct) is not { } typeName) return;
        // GetTypeByMetadataName resolves fully-qualified names to a type in this compilation or its refs;
        // the sink drops it if it turns out to be non-solution (invariant #5).
        if (compilation.GetTypeByMetadataName(typeName) is { } type)
            RootTypeAndMembers(type, sink);
    }

    /// <summary>Resolve a typeof(...) argument to its type and root it (with its members).</summary>
    private static void RootTypeFromTypeofArg(
        SemanticModel model, InvocationExpressionSyntax inv, IContributionSink sink, CancellationToken ct)
    {
        if (FirstArg(inv) is not TypeOfExpressionSyntax typeOf) return;
        if (model.GetTypeInfo(typeOf.Type, ct).Type is { } type)
            RootTypeAndMembers(type, sink);
    }

    /// <summary>
    /// Root a reflectively-named type AND its effectively externally-visible declared members. A type resolved from
    /// a reflection string (Type.GetType / Activator.CreateInstance) is being instantiated and its members
    /// invoked at runtime; rooting the members keeps them alive (mirrors the entry-type rule). Over-rooting
    /// here is a false negative at worst (§3.8) and is scoped to THIS type, not its collaborators.
    /// </summary>
    private static void RootTypeAndMembers(ITypeSymbol type, IContributionSink sink)
    {
        sink.AddRoot(type);
        foreach (var member in type.GetMembers())
            if (!member.IsImplicitlyDeclared && SymbolVisibility.IsExternallyVisible(member))
                sink.AddRoot(member);
    }

    /// <summary>
    /// Resolve <c>receiver.GetMethod("X")</c> (etc.) to a member on the receiver's type and root it.
    /// The receiver may be <c>typeof(T)</c>, <c>x.GetType()</c>, or any expression whose type resolves.
    /// </summary>
    private static void RootMemberFromString(
        SemanticModel model, InvocationExpressionSyntax inv, string reflectionMethod,
        IContributionSink sink, CancellationToken ct)
    {
        if (ConstStringArg(model, inv, 0, ct) is not { } memberName) return;
        if (ReceiverType(model, inv, ct) is not { } targetType) return;

        var kind = reflectionMethod switch
        {
            "GetMethod" => SymbolKind.Method,
            "GetProperty" => SymbolKind.Property,
            "GetField" => SymbolKind.Field,
            "GetEvent" => SymbolKind.Event,
            _ => (SymbolKind?)null,
        };
        if (kind is null) return;

        // Root every matching member by that name (overloads/shadows) on the type and its bases — the
        // reflected member could be any of them; over-rooting here can at worst be a false negative.
        for (var t = targetType; t is not null; t = t.BaseType)
            foreach (var member in t.GetMembers(memberName))
                if (member.Kind == kind)
                    sink.AddRoot(member);
    }

    /// <summary>The type the reflection call is invoked on: typeof(T), x.GetType(), or a Type expression.</summary>
    private static ITypeSymbol? ReceiverType(SemanticModel model, InvocationExpressionSyntax inv, CancellationToken ct)
    {
        if (inv.Expression is not MemberAccessExpressionSyntax { Expression: { } receiver }) return null;

        // typeof(T).GetMethod(...) — the T itself is the target type.
        if (receiver is TypeOfExpressionSyntax typeOf)
            return model.GetTypeInfo(typeOf.Type, ct).Type;

        // <inner>.GetType().GetMethod(...) — resolve the type <inner> reflects. GetType() is a
        // parameterless instance method returning System.Type (Object.GetType; Roslyn may attribute it
        // to the receiver's own type when that type shadows the signature — accept either).
        if (receiver is InvocationExpressionSyntax innerInv
            && innerInv.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetType", Expression: { } innerReceiver }
            && model.GetSymbolInfo(innerInv, ct).Symbol is IMethodSymbol { Name: "GetType", IsStatic: false, Parameters.IsEmpty: true } getType
            && SystemTypeIdentities.Any(identity =>
                SymbolIdentity.MatchesType(getType.ReturnType as INamedTypeSymbol, identity)))
        {
            var innerType = model.GetTypeInfo(innerReceiver, ct).Type;
            // Idiomatic `instance.GetType()` — the instance's static type is the conservative target.
            if (innerType is not null && innerType.SpecialType != SpecialType.System_Object
                && !SystemTypeIdentities.Any(identity =>
                    SymbolIdentity.MatchesType(innerType as INamedTypeSymbol, identity)))
                return innerType;

            // `typeVar.GetType()` where the receiver is itself a System.Type value: the runtime type is
            // RuntimeType, not what the string member lives on. Trace the value back to a typeof(T).
            return TypeofTargetOf(model, innerReceiver, ct);
        }

        return null;
    }

    /// <summary>
    /// If <paramref name="expression"/> is (or resolves to) a <c>typeof(T)</c> value, return T. Handles a
    /// direct typeof and a local/field/parameter initialized from a single typeof in the same declaration.
    /// Conservative: returns null when the value can't be pinned to one typeof.
    /// </summary>
    private static ITypeSymbol? TypeofTargetOf(SemanticModel model, ExpressionSyntax expression, CancellationToken ct)
    {
        if (expression is TypeOfExpressionSyntax typeOf)
            return model.GetTypeInfo(typeOf.Type, ct).Type;

        // A local/field whose declarator initializer is `typeof(T)` — the common `var t = typeof(T)` shape.
        // Only when the declaration is in THIS tree (the semantic model is tree-scoped).
        if (model.GetSymbolInfo(expression, ct).Symbol is { } symbol)
            foreach (var reference in symbol.DeclaringSyntaxReferences)
                if (reference.SyntaxTree == expression.SyntaxTree
                    && reference.GetSyntax(ct) is VariableDeclaratorSyntax { Initializer.Value: TypeOfExpressionSyntax init })
                    return model.GetTypeInfo(init.Type, ct).Type;

        return null;
    }

    private static string? ConstStringArg(SemanticModel model, InvocationExpressionSyntax inv, int index, CancellationToken ct)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count <= index) return null;
        // Accept any compile-time constant string (literal or const field) — not just a bare literal.
        var constant = model.GetConstantValue(args[index].Expression, ct);
        return constant is { HasValue: true, Value: string s } && s.Length > 0 ? s : null;
    }

    private static ExpressionSyntax? FirstArg(InvocationExpressionSyntax inv) =>
        inv.ArgumentList.Arguments.Count > 0 ? inv.ArgumentList.Arguments[0].Expression : null;
}
