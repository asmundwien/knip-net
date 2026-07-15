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

    /// <summary>
    /// Ids DECLARED inside a BUILT-IN generated tree (H11). These are still walked for their outbound
    /// edges/roots (so user symbols they reference stay alive), but are NEVER reported as dead — the
    /// user did not author that code. String-keyed by <see cref="SymbolId"/> (invariant #1). Consulted
    /// by DeadCodeAnalyzer.ShouldReport, mirroring the existing ignore-for-reporting path.
    /// </summary>
    public HashSet<string> GeneratedDeclarations { get; } = new(StringComparer.Ordinal);

    /// <summary>Count of references to unresolved (error) types — a signal the solution isn't fully restored.</summary>
    public int UnresolvedTypeReferences;

    /// <summary>
    /// Per source-project usage of OTHER solution assemblies: source assembly NAME → the set of
    /// other solution assembly NAMEs whose symbols that project's code actually touches. Populated
    /// from cross-assembly edges as the walk runs; consumed to detect unused &lt;ProjectReference&gt;s.
    /// String-keyed by assembly name (invariant #1) — no symbol-reference-keyed collections.
    /// </summary>
    public Dictionary<string, HashSet<string>> UsedAssemblies { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Per source-project usage of EXTERNAL (non-solution) assemblies: source assembly NAME → the set
    /// of external assembly NAMEs (BCL / NuGet) whose symbols that project's code touches. Mirrors
    /// <see cref="UsedAssemblies"/> but for the NON-solution branch that <see cref="AddEdge"/> otherwise
    /// drops (invariant #5: those symbols are NOT graph nodes — only the assembly NAME is recorded, as a
    /// string, invariant #1). Consumed to detect unused &lt;PackageReference&gt;s: a package whose
    /// delivered assemblies never appear here is never touched by any symbol.
    /// </summary>
    public Dictionary<string, HashSet<string>> UsedExternalAssemblies { get; } = new(StringComparer.Ordinal);

    public void AddEdge(string source, string target)
    {
        if (!Edges.TryGetValue(source, out var set))
            Edges[source] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(target);
    }

    /// <summary>Record that <paramref name="sourceAssembly"/>'s code references a symbol in <paramref name="targetAssembly"/>.</summary>
    public void RecordAssemblyUse(string sourceAssembly, string targetAssembly)
    {
        if (!UsedAssemblies.TryGetValue(sourceAssembly, out var set))
            UsedAssemblies[sourceAssembly] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(targetAssembly);
    }

    /// <summary>
    /// Record that <paramref name="sourceAssembly"/>'s code references a symbol OWNED by the external
    /// (non-solution) assembly <paramref name="externalAssembly"/>. The external symbol is NOT added to
    /// the graph (invariant #5) — only the assembly name is retained (invariant #1) to reason about
    /// unused &lt;PackageReference&gt;s.
    /// </summary>
    public void RecordExternalAssemblyUse(string sourceAssembly, string externalAssembly)
    {
        if (!UsedExternalAssemblies.TryGetValue(sourceAssembly, out var set))
            UsedExternalAssemblies[sourceAssembly] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(externalAssembly);
    }
}
