using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Knip.Core.Analysis;

/// <summary>
/// Collects runtime-only hazard shapes that make a finding unsafe to auto-delete because deletion can
/// survive build + tests and fail at runtime: serializer-reflected data, configuration-bound properties,
/// and activation dependencies of DI registrations whose overload does not prove container construction.
/// <see cref="FindingEnrichment"/> turns those shapes into advisory hazards and
/// <see cref="ConfidenceModel"/> demotes them to low.
/// <para>
/// This is detection, not reachability. It runs independently of optional keep-alive plugins because
/// hazards must survive a disabled plugin. Recognition is deliberately conservative: false hazard positives
/// only reduce autonomy; false negatives can authorize unsafe deletion.
/// </para>
/// </summary>
internal static class RuntimeHazardDetector
{
    // Serialize/deserialize methods whose resolved target type's data members a serializer reflects over.
    private static readonly HashSet<string> SerializerMethodNames = new(StringComparer.Ordinal)
    {
        "Serialize", "Deserialize",           // System.Text.Json.JsonSerializer.Serialize/Deserialize<T>
        "SerializeObject", "DeserializeObject", // Newtonsoft.Json.JsonConvert.SerializeObject/DeserializeObject<T>
    };

    // Containing-type simple names that host the recognized serializer entry points.
    private static readonly HashSet<string> SerializerContainingTypeNames = new(StringComparer.Ordinal)
    {
        "JsonSerializer", "JsonConvert",
    };

    // Config-binding methods + the containing types that host them (Microsoft.Extensions.Configuration /
    // .Options). Get<T>()/GetSection(...).Get<T>() and Bind(instance) live on ConfigurationBinder (surfaced
    // as extensions on IConfiguration/IConfigurationSection); Configure<T>(section) lives on the options
    // service-collection extensions.
    private static readonly HashSet<string> ConfigBinderContainingTypeNames = new(StringComparer.Ordinal)
    {
        "ConfigurationBinder", "IConfiguration", "IConfigurationSection",
        "OptionsConfigurationServiceCollectionExtensions", "OptionsServiceCollectionExtensions",
    };

    /// <summary>
    /// Walk one project's syntax trees for serializer, config-binding, and DI registration shapes. Records
    /// type targets plus uncertain DI activation roots; activation closures are completed after the full
    /// solution graph exists. Additive across projects; never mutates reachability.
    /// </summary>
    public static void Collect(Compilation compilation, GraphState state, CancellationToken ct)
    {
        var genericConfigHelpers = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);

            foreach (var inv in tree.GetRoot(ct).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method) continue;

                if (DependencyInjectionRegistration.TryResolve(model, inv, method, ct, out var registration)
                    && !registration.ActivatesConstructors)
                    RecordActivationRoots(registration.ImplementationType, state);
                var containingName = method.ContainingType?.Name;
                if (containingName is null) continue;

                // Serialization USAGE: a recognized call → its target and collection element types.
                if (SerializerMethodNames.Contains(method.Name)
                    && SerializerContainingTypeNames.Contains(containingName))
                {
                    foreach (var type in SerializedTypeTraversal.SelfAndCollectionElements(
                        TargetType(model, inv, method, ct)))
                        Record(type, state.SerializationUsageTypes);
                    continue;
                }

                // CONFIG binding: Get<T>() / GetSection(...).Get<T>() / Configure<T>(section) resolve their
                // type ARGUMENT; Bind(instance) resolves the bound VALUE's type (its last argument).
                if (!ConfigBinderContainingTypeNames.Contains(containingName)) continue;
                switch (method.Name)
                {
                    case "Get" or "Configure":
                        {
                            var target = TargetType(model, inv, method, ct);
                            RecordConfigBoundType(target, state.ConfigBoundTypes);
                            RecordGenericHelper(target, genericConfigHelpers);
                            break;
                        }
                    case "Bind":
                        RecordConfigBoundType(LastArgumentType(model, inv, ct), state.ConfigBoundTypes);
                        break;
                }
            }
        }

        if (genericConfigHelpers.Count == 0) return;

        // A binder call inside Helper<T> resolves only to the method type parameter in that syntax tree.
        // Resolve each closed Helper<Concrete> call site so the concrete binding target receives the hazard.
        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);

            foreach (var inv in tree.GetRoot(ct).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method
                    || SymbolId.For(method) is not { } methodId
                    || !genericConfigHelpers.TryGetValue(methodId, out var parameterOrdinals))
                    continue;

                foreach (var ordinal in parameterOrdinals)
                    if (ordinal < method.TypeArguments.Length)
                        RecordConfigBoundType(method.TypeArguments[ordinal], state.ConfigBoundTypes);
            }
        }
    }

    private static void RecordActivationRoots(INamedTypeSymbol type, GraphState state)
    {
        foreach (var entryPoint in RuntimeActivation.EntryPoints(type))
        {
            if (SymbolId.For(entryPoint) is not { } entryPointId)
                continue;

            state.DiPluginActivationRoots.Add(entryPointId);
            if (RuntimeActivation.HasInstanceInitializer(entryPoint))
                state.DiPluginShapedSymbols.Add(entryPointId);
        }

        foreach (var activatedType in RuntimeActivation.TypeChain(type))
            if (SymbolId.For(activatedType) is { } typeId)
                state.DiPluginActivationTypes.Add(typeId);
    }

    public static void CompleteDiPluginClosures(GraphState state)
    {
        RuntimeActivation.CompleteRoots(state);
        foreach (var typeId in state.DiPluginActivationTypes)
        {
            if (!state.RuntimeInitializersByType.TryGetValue(typeId, out var initializers))
                continue;

            state.DiPluginActivationRoots.UnionWith(initializers);
            state.DiPluginShapedSymbols.UnionWith(initializers);
        }

        var pending = new Stack<string>();
        foreach (var activationRoot in state.DiPluginActivationRoots)
            if (state.Edges.TryGetValue(activationRoot, out var targets))
                foreach (var target in targets)
                    pending.Push(target);

        while (pending.Count > 0)
        {
            var symbol = pending.Pop();
            if (!state.DiPluginShapedSymbols.Add(symbol)
                || !state.Edges.TryGetValue(symbol, out var targets))
                continue;

            foreach (var target in targets)
                pending.Push(target);
        }
    }

    private static void RecordGenericHelper(
        ITypeSymbol? target, Dictionary<string, HashSet<int>> genericConfigHelpers)
    {
        if (target is not ITypeParameterSymbol
            {
                TypeParameterKind: TypeParameterKind.Method,
                ContainingSymbol: IMethodSymbol helper,
            } parameter
            || SymbolId.For(helper) is not { } helperId)
            return;

        if (!genericConfigHelpers.TryGetValue(helperId, out var ordinals))
        {
            ordinals = [];
            genericConfigHelpers.Add(helperId, ordinals);
        }

        ordinals.Add(parameter.Ordinal);
    }

    /// <summary>
    /// The type a call targets: its single explicit type argument if present (<c>Get&lt;T&gt;</c>,
    /// <c>Deserialize&lt;T&gt;</c>), else the static type of the first serialized VALUE argument
    /// (<c>SerializeObject(dto)</c>). Null when neither resolves (conservative).
    /// </summary>
    private static ITypeSymbol? TargetType(
        SemanticModel model, InvocationExpressionSyntax inv, IMethodSymbol method, CancellationToken ct)
    {
        if (method.TypeArguments.Length == 1 && method.TypeArguments[0] is { TypeKind: not TypeKind.Error } arg)
            return arg;

        if (inv.ArgumentList.Arguments.Count > 0
            && model.GetTypeInfo(inv.ArgumentList.Arguments[0].Expression, ct).Type is { TypeKind: not TypeKind.Error } valueType)
            return valueType;

        return null;
    }

    /// <summary>The static type of the LAST argument (the bound instance in <c>config.Bind(instance)</c>).</summary>
    private static ITypeSymbol? LastArgumentType(
        SemanticModel model, InvocationExpressionSyntax inv, CancellationToken ct)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count == 0) return null;
        return model.GetTypeInfo(args[^1].Expression, ct).Type is { TypeKind: not TypeKind.Error } type
            ? type
            : null;
    }

    private static void RecordConfigBoundType(ITypeSymbol? type, HashSet<string> set)
    {
        Record(type, set);

        for (var baseType = (type as INamedTypeSymbol)?.BaseType;
             baseType is not null && baseType.SpecialType != SpecialType.System_Object;
             baseType = baseType.BaseType)
            Record(baseType, set);
    }

    private static void Record(ITypeSymbol? type, HashSet<string> set)
    {
        if (type is not null && SymbolId.For(type) is { } id) set.Add(id);
    }
}
