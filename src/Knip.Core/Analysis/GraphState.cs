using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>
/// The shared reachability graph accumulated across all projects, keyed by <see cref="SymbolId"/>
/// so the same logical symbol unifies across project (source ↔ metadata) boundaries.
/// </summary>
internal sealed class GraphState
{
    /// <summary>
    /// When true the walker records a representative source <see cref="Location"/> per (source→target)
    /// edge into <see cref="EdgeSources"/>, so <c>--why</c> can render "referenced at file:line". OFF by
    /// default — provenance costs memory (invariant, WS8c §5.2), so it is gated behind the CLI flag and
    /// the default run keeps its current memory profile. NEVER surfaces graph keys (invariant #1): the
    /// map is consulted only to resolve a hop to a display name + file:line inside Core.
    /// </summary>
    public bool CaptureProvenance { get; init; }

    /// <summary>Symbol id → a representative declaring symbol (for reporting).</summary>
    public Dictionary<string, ISymbol> Declared { get; } = new(StringComparer.Ordinal);

    /// <summary>Symbol id → ids it references ("uses" edges).</summary>
    public Dictionary<string, HashSet<string>> Edges { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// (WS8c, gated by <see cref="CaptureProvenance"/>) One representative reference-site source location
    /// per edge, keyed "source␟target". Populated only when provenance is requested; consumed by the
    /// <c>--why</c> report to render a hop's <c>file:line</c>. Empty on a default run.
    /// </summary>
    public Dictionary<string, Location> EdgeSources { get; } = new(StringComparer.Ordinal);

    /// <summary>Record a representative reference-site location for an edge (no-op unless provenance is on).</summary>
    public void RecordEdgeSource(string source, string target, Location? location)
    {
        if (!CaptureProvenance || location is null || !location.IsInSource) return;
        var key = source + "" + target;
        if (!EdgeSources.ContainsKey(key)) EdgeSources[key] = location;
    }

    /// <summary>Root ids: framework entry points reachability starts from.</summary>
    public HashSet<string> Roots { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// (WS7) Roots whose ORIGIN is a test: a root seeded while walking a TEST project, or a root seeded
    /// from a test attribute (<c>[Fact]</c>/<c>[Theory]</c>/…) in any project. May overlap
    /// <see cref="ProductionRoots"/> (an id rooted from both a test and a production site); the net
    /// TEST-ONLY roots are <see cref="TestOnlyRoots"/>. String-keyed (invariant #1).
    /// </summary>
    public HashSet<string> TestRoots { get; } = new(StringComparer.Ordinal);

    /// <summary>(WS7) Roots whose ORIGIN is production (a non-test root site). See <see cref="TestRoots"/>.</summary>
    public HashSet<string> ProductionRoots { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// (WS7) Ids DECLARED in a TEST project. These are test CODE, never OnlyUsedByTests production
    /// findings (the two-color pass excludes them). String-keyed (invariant #1).
    /// </summary>
    public HashSet<string> TestDeclarations { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// (WS7) The roots that are TEST-ONLY: test-origin and NOT also production-rooted. Production wins so a
    /// symbol rooted from any production site keeps its whole closure alive; the two-color traversal seeds
    /// its PRODUCTION color from <c>Roots \ TestOnlyRoots</c> and its FULL color from all <see cref="Roots"/>.
    /// </summary>
    public IEnumerable<string> TestOnlyRoots => TestRoots.Where(r => !ProductionRoots.Contains(r));

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
