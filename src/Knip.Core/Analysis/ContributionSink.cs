using Knip.Core.Plugins;
using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>
/// The single choke point through which a plugin mutates the graph. Symbol-typed on the way in; it
/// owns key derivation (<see cref="SymbolId.For"/>) and the solution-assembly filter, so a plugin
/// physically cannot violate invariant #1 (never sees a string key) or invariant #5 (never edges to
/// a non-solution target). Every method is additive-only — there is no remove/suppress verb, so a
/// buggy plugin can only cause a false negative, never a false positive (§3.8).
/// </summary>
internal sealed class ContributionSink : IContributionSink
{
    private readonly GraphState _state;
    // ISet (not IReadOnlySet) is the common surface across net10.0 and net472 BCLs — mirrors ReferenceWalker.
    private readonly ISet<string> _solutionAssemblies;
    private readonly bool _testProject;


    public ContributionSink(GraphState state, ISet<string> solutionAssemblies, bool testProject)
    {
        _state = state;
        _solutionAssemblies = solutionAssemblies;
        _testProject = testProject;
    }

    /// <summary>Roots added by this plugin pass (observability, -v).</summary>
    public int RootsAdded { get; private set; }

    /// <summary>Edges added by this plugin pass (observability, -v).</summary>
    public int EdgesAdded { get; private set; }


    public void AddRoot(ISymbol symbol)
    {
        if (!IsSolutionDefined(symbol)) return;            // invariant #5: only solution symbols are nodes
        if (SymbolId.For(symbol) is { } id && _state.AddRoot(id, _testProject)) // invariant #1: key derivation owned here
            RootsAdded++;
    }

    internal void RequestRuntimeActivation(INamedTypeSymbol type)
    {
        foreach (var activatedType in RuntimeActivation.TypeChain(type))
        {
            if (!IsSolutionDefined(activatedType) || SymbolId.For(activatedType) is not { } typeId) continue;
            var roots = _testProject
                ? _state.TestRuntimeActivationRootTypes
                : _state.ProductionRuntimeActivationRootTypes;
            roots.Add(typeId);
        }
    }

    public void AddEdge(ISymbol from, ISymbol to)
    {
        if (!IsSolutionDefined(to)) return;                // invariant #5 (same rule as ReferenceWalker.AddEdge)
        if (SymbolId.For(from) is { } f && SymbolId.For(to) is { } t &&
            !string.Equals(f, t, StringComparison.Ordinal))
        {
            _state.AddPluginEdge(f, t, _testProject);
            EdgesAdded++;
        }
    }

    private bool IsSolutionDefined(ISymbol symbol) =>
        symbol.OriginalDefinition.ContainingAssembly?.Name is { } a && _solutionAssemblies.Contains(a);
}
