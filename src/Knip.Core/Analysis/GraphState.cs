using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>
/// The shared reachability graph accumulated across all projects, keyed by <see cref="SymbolId"/>
/// so the same logical symbol unifies across project (source ↔ metadata) boundaries.
/// </summary>
internal sealed class GraphState
{
    /// <summary>Symbol id → a representative declaring symbol (for reporting).</summary>
    public Dictionary<string, ISymbol> Declared { get; } = new(StringComparer.Ordinal);

    /// <summary>Symbol id → ids it references ("uses" edges).</summary>
    public Dictionary<string, HashSet<string>> Edges { get; } = new(StringComparer.Ordinal);

    /// <summary>Root ids: framework entry points reachability starts from.</summary>
    public HashSet<string> Roots { get; } = new(StringComparer.Ordinal);

    /// <summary>Count of references to unresolved (error) types — a signal the solution isn't fully restored.</summary>
    public int UnresolvedTypeReferences;

    public void AddEdge(string source, string target)
    {
        if (!Edges.TryGetValue(source, out var set))
            Edges[source] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(target);
    }
}
