using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Knip.Core.Analysis;

/// <summary>
/// (RB-01 Task B) Collects the runtime-only hazard SHAPES that make a dead-code finding risky to
/// auto-delete because the deletion survives build + tests but breaks at RUNTIME (invariant #8, the
/// sacred residual): a DTO property read only by a JSON serializer, or a POCO property populated only by
/// configuration binding. It records, per solution TYPE, whether that type is a serializer target or a
/// config-binding target; <see cref="FindingEnrichment"/> turns those into advisory hazards on the type's
/// dead data-member findings, and <see cref="ConfidenceModel"/> demotes them to low.
/// <para>
/// This is DETECTION, not reachability: it runs in the analysis layer independently of the opt-in
/// <c>serialization</c> plugin (which ADDS ROOTS to keep members alive). Hazards must be attached even when
/// that plugin is disabled — they are advisory metadata, not keep-alive edges. Recognition is NAME-BASED and
/// conservative (well-known method + containing-type simple names), so it needs no NuGet reference by Knip
/// itself (invariant #9). False hazard positives are cheap (they never change the emitted set); false hazard
/// negatives are the expensive direction — when in doubt, tag.
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
    /// Walk one project's syntax trees for recognized serializer / config-binding calls and record the
    /// resolved target TYPE (by <see cref="SymbolId"/>) into the shared graph state. Additive across
    /// projects; never mutates reachability.
    /// </summary>
    public static void Collect(Compilation compilation, GraphState state, CancellationToken ct)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);

            foreach (var inv in tree.GetRoot(ct).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method) continue;
                var containingName = method.ContainingType?.Name;
                if (containingName is null) continue;

                // Serialization USAGE: a recognized serialize/deserialize call → its target type.
                if (SerializerMethodNames.Contains(method.Name)
                    && SerializerContainingTypeNames.Contains(containingName))
                {
                    Record(TargetType(model, inv, method, ct), state.SerializationUsageTypes);
                    continue;
                }

                // CONFIG binding: Get<T>() / GetSection(...).Get<T>() / Configure<T>(section) resolve their
                // type ARGUMENT; Bind(instance) resolves the bound VALUE's type (its last argument).
                if (!ConfigBinderContainingTypeNames.Contains(containingName)) continue;
                switch (method.Name)
                {
                    case "Get" or "Configure":
                        Record(TargetType(model, inv, method, ct), state.ConfigBoundTypes);
                        break;
                    case "Bind":
                        Record(LastArgumentType(model, inv, ct), state.ConfigBoundTypes);
                        break;
                }
            }
        }
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

    private static void Record(ITypeSymbol? type, HashSet<string> set)
    {
        if (type is not null && SymbolId.For(type) is { } id) set.Add(id);
    }
}
