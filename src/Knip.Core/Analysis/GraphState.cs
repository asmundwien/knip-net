using Knip.Core.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    /// Plugin edges discovered only while analyzing test projects. Full traversal follows them; production
    /// traversal does not. An ordinary source edge or production plugin discovery removes the matching entry.
    /// </summary>
    private Dictionary<string, HashSet<string>> TestOnlyPluginEdges { get; } = new(StringComparer.Ordinal);


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
    /// Test-origin roots that are themselves test entry code. Unlike plugin roots discovered from a test
    /// project, these are excluded from production findings even when declared in a production project.
    /// </summary>
    public HashSet<string> TestEntryRoots { get; } = new(StringComparer.Ordinal);


    /// <summary>
    /// (WS7) The roots that are TEST-ONLY: test-origin and NOT also production-rooted. Production wins so a
    /// symbol rooted from any production site keeps its whole closure alive; the two-color traversal seeds
    /// its PRODUCTION color from <c>Roots \ TestOnlyRoots</c> and its FULL color from all <see cref="Roots"/>.
    /// </summary>
    public IEnumerable<string> TestOnlyRoots => TestRoots.Where(r => !ProductionRoots.Contains(r));
    public bool AddRoot(string id, bool asTest)
    {
        var added = Roots.Add(id);
        if (asTest) TestRoots.Add(id);
        else ProductionRoots.Add(id);
        return added;
    }


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

    /// <summary>
    /// (RB-01 Task B) Types whose data members are USAGE-shaped for serialization: each resolved target
    /// of a recognized serializer call (<c>JsonConvert.DeserializeObject&lt;T&gt;</c>,
    /// <c>JsonSerializer.Serialize/Deserialize&lt;T&gt;</c>, …), plus its collection element types. Keyed by
    /// <see cref="SymbolId"/> of the TYPE (invariant #1). Advisory only — drives the
    /// <see cref="Model.Hazard.SerializationShaped"/> tag on dead data-member findings; never changes
    /// reachability. Populated by <see cref="RuntimeHazardDetector"/>.
    /// </summary>
    public HashSet<string> SerializationUsageTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// (RB-01 Task B) Types bound from configuration: the type appears as the resolved target of a
    /// recognized binder call (<c>IConfiguration.Get&lt;T&gt;()</c> / <c>.Bind(instance)</c> /
    /// <c>Configure&lt;T&gt;</c>). Keyed by <see cref="SymbolId"/> of the TYPE. Advisory only — drives the
    /// <see cref="Model.Hazard.ConfigBoundType"/> tag on the type's dead public-property findings.
    /// </summary>
    public HashSet<string> ConfigBoundTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Symbols reachable only through runtime activation of DI-registered types whose activation is not
    /// statically proven. Advisory only; findings in this closure carry DiPluginShaped.
    /// </summary>
    public HashSet<string> DiPluginShapedSymbols { get; } = new(StringComparer.Ordinal);

    /// <summary>Source field/event-field initializers keyed by their containing type's stable symbol id.</summary>
    public Dictionary<string, HashSet<string>> RuntimeInitializersByType { get; } = new(StringComparer.Ordinal);

    /// <summary>Runtime-activated types contributed from test projects.</summary>
    public HashSet<string> TestRuntimeActivationRootTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>Runtime-activated types contributed from production projects.</summary>
    public HashSet<string> ProductionRuntimeActivationRootTypes { get; } = new(StringComparer.Ordinal);


    /// <summary>Types with uncertain DI activation whose source initializer closures need hazard tags.</summary>
    public HashSet<string> DiPluginActivationTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>Runtime-activation roots whose uncertain DI closures are finalized after graph build.</summary>
    public HashSet<string> DiPluginActivationRoots { get; } = new(StringComparer.Ordinal);

    public void AddEdge(string source, string target)
    {
        AddEdge(Edges, source, target);
        RemoveTestOnlyPluginEdge(source, target);
    }

    public void AddPluginEdge(string source, string target, bool asTest)
    {
        var alreadyPresent = Edges.TryGetValue(source, out var targets) && targets.Contains(target);
        AddEdge(Edges, source, target);

        if (!asTest)
            RemoveTestOnlyPluginEdge(source, target);
        else if (!alreadyPresent)
            AddEdge(TestOnlyPluginEdges, source, target);
    }

    public bool IsTestOnlyPluginEdge(string source, string target) =>
        TestOnlyPluginEdges.TryGetValue(source, out var targets) && targets.Contains(target);

    private static void AddEdge(Dictionary<string, HashSet<string>> edges, string source, string target)
    {
        if (!edges.TryGetValue(source, out var targets))
            edges[source] = targets = new HashSet<string>(StringComparer.Ordinal);
        targets.Add(target);
    }

    private void RemoveTestOnlyPluginEdge(string source, string target)
    {
        if (!TestOnlyPluginEdges.TryGetValue(source, out var targets)) return;
        targets.Remove(target);
        if (targets.Count == 0) TestOnlyPluginEdges.Remove(source);
    }

    public void RecordRuntimeInitializer(string type, string initializer)
    {
        if (!RuntimeInitializersByType.TryGetValue(type, out var entries))
            RuntimeInitializersByType[type] = entries = new HashSet<string>(StringComparer.Ordinal);
        entries.Add(initializer);
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

/// <summary>
/// Defines the source entry points executed when a runtime creates an instance: every explicit instance
/// constructor and every instance field/event-field initializer on the concrete type and its applicable
/// base-type chain. Initializers remain activation roots when a constructor is implicit and therefore has no
/// source declaration of its own. <see cref="object"/> is never an analysis root.
/// </summary>
internal static class RuntimeActivation
{
    public static IEnumerable<INamedTypeSymbol> TypeChain(INamedTypeSymbol type)
    {
        for (var current = type;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
            yield return current;
    }

    public static IEnumerable<ISymbol> EntryPoints(INamedTypeSymbol type)
    {
        foreach (var current in TypeChain(type))
        {
            foreach (var constructor in current.InstanceConstructors)
                if (!constructor.IsImplicitlyDeclared)
                    yield return constructor;

            foreach (var member in current.GetMembers())
                if (HasInstanceInitializer(member))
                    yield return member;
        }
    }

    public static void AddRoots(INamedTypeSymbol type, IContributionSink sink)
    {
        foreach (var entryPoint in EntryPoints(type))
            sink.AddRoot(entryPoint);

        if (sink is ContributionSink contributionSink)
            contributionSink.RequestRuntimeActivation(type);
    }

    public static void CompleteRoots(GraphState state)
    {
        CompleteRoots(state, state.ProductionRuntimeActivationRootTypes, asTest: false);
        CompleteRoots(state, state.TestRuntimeActivationRootTypes, asTest: true);
    }

    private static void CompleteRoots(GraphState state, IEnumerable<string> typeIds, bool asTest)
    {
        foreach (var typeId in typeIds)
            if (state.RuntimeInitializersByType.TryGetValue(typeId, out var initializers))
                foreach (var initializer in initializers)
                    state.AddRoot(initializer, asTest);
    }

    public static bool HasInstanceInitializer(ISymbol member)
    {
        if (member.IsStatic || member is not (IFieldSymbol or IEventSymbol))
            return false;

        foreach (var syntaxReference in member.DeclaringSyntaxReferences)
            if (syntaxReference.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null })
                return true;

        return false;
    }
}
