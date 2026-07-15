using System.Diagnostics;
using Knip.Core.Configuration;
using Knip.Core.Model;
using Knip.Core.Plugins;
using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>
/// Solution-wide dead-code detector: builds a reachability graph over all projects, marks everything
/// reachable from the configured roots, and reports declared-but-unreachable symbols.
/// </summary>
public sealed class DeadCodeAnalyzer
{
    private static readonly SymbolDisplayFormat DisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private readonly KnipConfig _config;

    // Built-in plugins resolved once from config: only those enabled, in registry order.
    private readonly IReadOnlyList<(PluginDescriptor Descriptor, IKnipPlugin Plugin)> _plugins;

    public DeadCodeAnalyzer(KnipConfig config)
    {
        _config = config;
        _plugins = PluginRegistry.All
            .Where(config.IsPluginEnabled)
            .Select(d => (d, d.Factory()))
            .ToList();
    }

    public async Task<AnalysisResult> AnalyzeAsync(
        Solution solution, IProgress<string>? progress, CancellationToken ct, bool captureProvenance = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var state = new GraphState { CaptureProvenance = captureProvenance };
        var result = new AnalysisResult();

        var projects = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp && !Glob.IsMatchAny(p.Name, _config.Ignore.Projects))
            .ToList();

        // Assemblies whose symbols count as "in the solution" — edges to anything else (BCL, NuGet)
        // are dropped. Precomputed so cross-project edges resolve regardless of analysis order.
        var solutionAssemblies = projects
            .Select(p => p.AssemblyName)
            .ToHashSet(StringComparer.Ordinal);

        // Config typos surface as VISIBLE warnings rather than silently no-opping. Two channels, same
        // list: (1) unknown TOP-LEVEL and NESTED keys anywhere in knip.json (WS8c / L7); (2) unknown
        // plugin ids / per-plugin setting keys (the plugin-value validation ValidateKeys does not descend).
        foreach (var warning in _config.ValidateKeys())
            result.LoadDiagnostics.Add(warning);
        foreach (var warning in _config.ValidatePlugins())
            result.LoadDiagnostics.Add(warning);

        // The single choke point through which every plugin mutates the graph (invariants #1, #5, add-only).
        var sink = new ContributionSink(state, solutionAssemblies);

        // Per-DEFINING-assembly: does that project carry [InternalsVisibleTo] a NON-solution assembly?
        // Keyed by assembly name (invariant #1); drives the InternalsVisibleTo hazard in ToFinding.
        var ivtToNonSolution = new HashSet<string>(StringComparer.Ordinal);

        // WS7: per-project test/production classification (recorded for reliability + -v).
        var classifications = new List<ProjectClassification>();

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Analyzing {project.Name}");
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;
            result.ProjectsAnalyzed++;

            if (FindingEnrichment.HasInternalsVisibleToNonSolutionAssembly(compilation, solutionAssemblies))
                ivtToNonSolution.Add(compilation.Assembly.Name);

            // WS7: classify the project (production vs test) and record WHICH signal decided it (surfaced
            // via -v + reliability). Drives two-color root origin; only meaningful in production mode, but
            // classification is cheap and the -v report is useful either way.
            var classification = TestProjectClassifier.Classify(project, compilation, _config);
            classifications.Add(classification);
            var isTestProject = classification.Kind == ProjectKind.Test;
            progress?.Report(
                $"project {project.Name}: {classification.Kind.ToString().ToLowerInvariant()} " +
                $"(signal: {classification.Signal})");

            var isPublicApi = Glob.IsMatchAny(project.Name, _config.Roots.PublicApiProjects);
            if (compilation.GetEntryPoint(ct) is { } entry)
            {
                // Program entry point roots take the project's origin (test project → test root).
                if (SymbolId.For(entry) is { } entryId) AddRoot(state, entryId, isTestProject);
                if (entry.ContainingType is { } host && SymbolId.For(host) is { } hostId)
                    AddRoot(state, hostId, isTestProject);
            }

            foreach (var tree in compilation.SyntaxTrees)
            {
                ct.ThrowIfCancellationRequested();
                var root = await tree.GetRootAsync(ct);

                // H11: BUILT-IN generated trees are WALKED (their outbound edges/roots keep the user
                // symbols they reference alive — extending G8 from symbols to files) but every symbol
                // they DECLARE is marked "never report" (recorded by the walker into
                // state.GeneratedDeclarations). Checked BEFORE ignore.files so this holds even when a
                // generated pattern also sits in the (default) ignore list.
                var isGenerated = GeneratedCode.IsGenerated(tree, root);

                // I1 (UNCHANGED): a user-configured ignore.files match that is NOT built-in generated is
                // dropped WHOLESALE — not walked, not reported.
                if (!isGenerated && Glob.IsMatchAny(tree.FilePath, _config.Ignore.Files)) continue;

                var model = compilation.GetSemanticModel(tree);
                var walker = new ReferenceWalker(
                    model, _config, isPublicApi, solutionAssemblies, state,
                    generatedTree: isGenerated, testProject: isTestProject);
                walker.Visit(root);
            }

            // Plugin pass: AFTER the core walk of this project (declared nodes exist to edge to),
            // BEFORE AddPolymorphismEdges/Traverse (contributions must feed reachability). Each plugin
            // pulls its own semantic models from `compilation`; the sink owns key derivation + #5 filter.
            foreach (var (descriptor, plugin) in _plugins)
            {
                ct.ThrowIfCancellationRequested();
                sink.ResetCounts();
                var pluginWatch = Stopwatch.StartNew();
                var context = new PluginContext(compilation, project, _config.PluginSettingsFor(descriptor.Id), sink);
                plugin.Contribute(context, ct);
                pluginWatch.Stop();
                // Observability (-v): per plugin, per project — contribution counts + wall-time. Routed
                // through the progress/stderr channel so machine output on stdout stays clean (J6 invariant).
                progress?.Report(
                    $"plugin {descriptor.Id} on {project.Name}: +{sink.RootsAdded} root(s), " +
                    $"+{sink.EdgesAdded} edge(s) in {pluginWatch.Elapsed.TotalMilliseconds:0}ms");
            }
        }

        AddPolymorphismEdges(state);

        result.SymbolsAnalyzed = state.Declared.Count;
        result.RootCount = state.Roots.Count;

        // Two-color reachability (WS7). FULL = reachable from ANY root (default liveness, unchanged). In
        // production mode PRODUCTION = reachable from production-origin roots only; a symbol dead in
        // PRODUCTION but alive in FULL is reachable ONLY via tests → OnlyUsedByTests. In default mode the
        // production set is unused (every FULL-reachable symbol stays alive — K1/B1 pinned).
        var reachable = Traverse(state, state.Roots);
        var productionReachable = _config.Production
            ? Traverse(state, state.Roots.Where(r => !state.TestRoots.Contains(r) || state.ProductionRoots.Contains(r)))
            : reachable;

        BuildFindings(state, reachable, productionReachable, result, ivtToNonSolution);
        BuildProjectReferenceFindings(projects, state, result);
        BuildPackageReferenceFindings(projects, state, result);
        SortFindings(result);

        // WS7: record classifications + the zero-test-project warning into the reliability block. In
        // production mode with NO test projects detected, warn LOUDLY (stderr via progress + machine
        // diagnostics) — every test-only symbol would otherwise flip to a finding. Never fails; exit codes
        // unchanged (invariant: production mode EMITS a finding class, it does not change exit semantics).
        result.Reliability.TestProjectClassifications.AddRange(classifications.Select(c =>
            new Model.TestProjectClassificationInfo(
                c.Project, c.Kind.ToString().ToLowerInvariant(), c.Signal)));
        if (_config.Production && classifications.All(c => c.Kind == ProjectKind.Production))
        {
            var warning =
                "production mode requested, but ZERO test projects were detected — no code will be " +
                "flagged OnlyUsedByTests. Configure 'testProjects' globs or check the solution. " +
                "(Classification signals per project are in reliability.testProjectClassifications.)";
            result.Reliability.ProductionModeWarnings.Add(warning);
        }

        if (state.UnresolvedTypeReferences > 0)
            result.LoadDiagnostics.Insert(0,
                $"{state.UnresolvedTypeReferences} reference(s) to unresolved types — the solution may not be " +
                "fully restored/buildable. Run 'dotnet restore' (and ensure private feeds are authenticated); " +
                "otherwise dead-code results can include false positives.");

        // Reliability block (WS8 §1.1): what the analyzer knows. Project-load/restore failures observed at
        // the workspace boundary are attributed by KnipEngine after this returns.
        result.Reliability.ProjectsLoaded = result.ProjectsAnalyzed;
        result.Reliability.UnresolvedTypeReferences = state.UnresolvedTypeReferences;

        // (WS8c) Retain the reachability graph for --why ONLY when provenance was requested; a default run
        // drops it (memory unchanged). WhyService renders keys → display names + file:line (invariant #1).
        if (captureProvenance)
            result.WhyContext = new WhyContext(state, reachable);

        result.Elapsed = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Keep overrides and interface implementations alive when the abstraction they satisfy is used,
    /// so virtual/interface dispatch doesn't produce false positives.
    /// </summary>
    private static void AddPolymorphismEdges(GraphState state)
    {
        foreach (var symbol in state.Declared.Values)
        {
            switch (symbol)
            {
                case IMethodSymbol { OverriddenMethod: { } m }: Link(state, m, symbol); break;
                case IPropertySymbol { OverriddenProperty: { } p }: Link(state, p, symbol); break;
                case IEventSymbol { OverriddenEvent: { } e }: Link(state, e, symbol); break;
            }

            if (symbol is INamedTypeSymbol type && type.TypeKind is not TypeKind.Interface)
            {
                foreach (var iface in type.AllInterfaces)
                    foreach (var member in iface.GetMembers())
                    {
                        var impl = type.FindImplementationForInterfaceMember(member);
                        if (impl is not null && !impl.IsImplicitlyDeclared)
                            Link(state, member, impl);
                    }
            }
        }
    }

    private static void Link(GraphState state, ISymbol from, ISymbol to)
    {
        if (SymbolId.For(from) is { } fromId && SymbolId.For(to) is { } toId)
            state.AddEdge(fromId, toId);
    }

    /// <summary>(WS7) Seed a root from the analyzer, recording its production/test origin.</summary>
    private static void AddRoot(GraphState state, string id, bool asTest)
    {
        state.Roots.Add(id);
        if (asTest) state.TestRoots.Add(id);
        else state.ProductionRoots.Add(id);
    }

    /// <summary>
    /// Mark-and-sweep BFS from the given <paramref name="roots"/> (WS7 two-color: called once for ALL
    /// roots — default liveness — and once for the production-only roots). Only declared symbols enter the
    /// reachable set; edges to non-declared ids are ignored.
    /// </summary>
    private static HashSet<string> Traverse(GraphState state, IEnumerable<string> roots)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        foreach (var root in roots)
            if (state.Declared.ContainsKey(root) && reachable.Add(root))
                queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!state.Edges.TryGetValue(current, out var targets)) continue;
            foreach (var target in targets)
                if (state.Declared.ContainsKey(target) && reachable.Add(target))
                    queue.Enqueue(target);
        }

        return reachable;
    }

    private void BuildFindings(
        GraphState state,
        HashSet<string> reachable,
        HashSet<string> productionReachable,
        AnalysisResult result,
        HashSet<string> ivtToNonSolution)
    {
        var dead = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, _) in state.Declared)
            if (!reachable.Contains(id)) dead.Add(id);

        // WS7 two-color: TEST-ONLY = alive in FULL but dead in PRODUCTION (reachable only through test
        // roots). Empty in default mode (productionReachable == reachable). These become OnlyUsedByTests
        // findings — a distinct kind whose remediation is "delete the code AND its tests" (K2/K5). Their
        // reporting is outermost-only within the test-only set (a test-only member of a test-only TYPE
        // reports the type). Test-side symbols themselves (test classes/methods) are NOT test-only —
        // they are dead only in production but they ARE the tests; we exclude anything whose defining
        // project is a test project by requiring the symbol be reachable via a PRODUCTION-origin path in
        // FULL... simpler: a test-only production symbol is one edged-to FROM the test side. We identify
        // them as {alive in FULL} \ {alive in PRODUCTION} \ {test-origin roots and their test-side-only
        // closure}. Concretely: exclude ids that are themselves test roots (the test methods/classes).
        var testOnly = new HashSet<string>(StringComparer.Ordinal);
        if (_config.Production)
            foreach (var (id, _) in state.Declared)
                if (reachable.Contains(id) && !productionReachable.Contains(id) && !IsTestSide(id, state))
                    testOnly.Add(id);

        // Graph key of each REPORTED dead symbol → its emitted finding id. A dead member whose containing
        // type is also dead is NOT reported (the outermost type is); rootCause resolution walks up to the
        // reported ancestor via this map. Two passes: emit findings first, then attribute rootCause.
        var reportedKeyToFindingId = new Dictionary<string, string>(StringComparer.Ordinal);
        var emitted = new List<(string Key, ISymbol Symbol, int Index)>();

        // Direct test-referrer index (K3): production symbol id → the test methods that reference it.
        var testReferrers = _config.Production
            ? BuildTestReferrers(state, testOnly)
            : new Dictionary<string, List<TestReferrer>>(StringComparer.Ordinal);

        // Outermost-only (§3.7) suppresses a member whose containing TYPE will itself be reported/deleted.
        // The deletion unit of an OnlyUsedByTests TYPE is the whole type — so a member that is plain-dead OR
        // test-only inside a test-only (or dead) type is subsumed. Suppress against dead ∪ testOnly.
        var suppressed = new HashSet<string>(dead, StringComparer.Ordinal);
        suppressed.UnionWith(testOnly);

        foreach (var (id, symbol) in state.Declared)
        {
            var isTestOnly = testOnly.Contains(id);
            if (!dead.Contains(id) && !isTestOnly) continue;
            // Outermost-only: a member reported/deleted via its containing type (dead or test-only) is
            // subsumed by the outer symbol.
            if (!ShouldReport(id, symbol, suppressed, state)) continue;
            var finding = isTestOnly
                ? ToTestOnlyFinding(symbol, ivtToNonSolution, testReferrers)
                : ToFinding(symbol, ivtToNonSolution);
            if (finding is null) continue;
            reportedKeyToFindingId[id] = finding.Id;
            emitted.Add((id, symbol, result.Findings.Count));
            result.Findings.Add(finding);
        }

        // Reverse edges over the DEAD set only: target key → dead source keys that reference it. A dead
        // referrer whose deletion would remove an incoming edge to this finding is its rootCause (L10).
        var incoming = BuildDeadIncomingEdges(state, dead);

        foreach (var (key, symbol, index) in emitted)
        {
            var rootCause = ResolveRootCause(key, symbol, incoming, dead, state, reportedKeyToFindingId);
            if (rootCause is not null)
                result.Findings[index] = result.Findings[index] with { RootCause = rootCause };
        }
    }

    /// <summary>target key → the set of DEAD source keys with an edge into it (incoming edges over dead).</summary>
    private static Dictionary<string, HashSet<string>> BuildDeadIncomingEdges(GraphState state, HashSet<string> dead)
    {
        var incoming = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (source, targets) in state.Edges)
        {
            if (!dead.Contains(source)) continue;
            foreach (var target in targets)
            {
                if (!dead.Contains(target)) continue;
                if (!incoming.TryGetValue(target, out var set))
                    incoming[target] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(source);
            }
        }
        return incoming;
    }

    /// <summary>
    /// (WS7) True when the id is TEST CODE (declared in a test project) or itself a test root — never an
    /// OnlyUsedByTests production finding. A test helper in a PRODUCTION project reached only by a test is
    /// NOT test-side (it is exactly the test-only production code we flag).
    /// </summary>
    private static bool IsTestSide(string id, GraphState state) =>
        state.TestDeclarations.Contains(id)
        || (state.TestRoots.Contains(id) && !state.ProductionRoots.Contains(id));

    /// <summary>
    /// (WS7 / K3) For each OnlyUsedByTests production symbol, the TEST methods that DIRECTLY reference it —
    /// the "and its tests" half of the deletion unit. A referrer is a source with an edge into the symbol
    /// that is itself a test root (an actual test method). Prose display names + file:line, never graph
    /// keys (invariant #1). Deterministic order (by display name).
    /// </summary>
    private static Dictionary<string, List<TestReferrer>> BuildTestReferrers(
        GraphState state, HashSet<string> testOnly)
    {
        var referrers = new Dictionary<string, List<TestReferrer>>(StringComparer.Ordinal);
        foreach (var (source, targets) in state.Edges)
        {
            // Only edges FROM an actual test method (a test-origin root) count as test referrers.
            if (!state.TestRoots.Contains(source) || state.ProductionRoots.Contains(source)) continue;
            if (!state.Declared.TryGetValue(source, out var sourceSymbol)) continue;

            foreach (var target in targets)
            {
                if (!testOnly.Contains(target)) continue;
                if (ToTestReferrer(sourceSymbol) is not { } referrer) continue;
                if (!referrers.TryGetValue(target, out var list))
                    referrers[target] = list = [];
                if (!list.Contains(referrer)) list.Add(referrer);
            }
        }

        foreach (var list in referrers.Values)
            list.Sort((a, b) => string.CompareOrdinal(a.Symbol, b.Symbol));
        return referrers;
    }

    private static TestReferrer? ToTestReferrer(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null) return null;
        var lineSpan = location.GetLineSpan();
        return new TestReferrer(
            symbol.ToDisplayString(DisplayFormat), lineSpan.Path, lineSpan.StartLinePosition.Line + 1);
    }

    /// <summary>
    /// The <see cref="Finding.Id"/> of the nearest DEAD symbol keeping this one dead (WS8 §L10): prefer the
    /// finding's own dead containing type; else a dead referrer (incoming edge) resolved to its reported
    /// ancestor. Null when directly unreferenced (no dead incoming edges and no dead containing type).
    /// </summary>
    private static string? ResolveRootCause(
        string key,
        ISymbol symbol,
        Dictionary<string, HashSet<string>> incoming,
        HashSet<string> dead,
        GraphState state,
        Dictionary<string, string> reportedKeyToFindingId)
    {
        // Prefer the containing type when it is itself dead (the deletion of the type removes this symbol).
        // NB: when the containing type is dead this member is NOT emitted (ShouldReport skips it), so this
        // branch only fires for reported members whose *type* is live but an enclosing scope is dead — rare;
        // kept for completeness per L10's "prefer the containing type".
        for (var container = symbol.ContainingType; container is not null; container = container.ContainingType)
            if (SymbolId.For(container) is { } cId && dead.Contains(cId)
                && reportedKeyToFindingId.TryGetValue(cId, out var containerFindingId))
                return containerFindingId;

        // Otherwise the nearest dead referrer whose deletion severs an incoming edge. Resolve each dead
        // source to its REPORTED finding (walk up to a reported ancestor); pick a deterministic one.
        if (incoming.TryGetValue(key, out var sources))
        {
            var ownFindingId = reportedKeyToFindingId.TryGetValue(key, out var own) ? own : null;
            foreach (var source in sources.OrderBy(s => s, StringComparer.Ordinal))
            {
                if (source == key) continue; // self-edge is not a cause
                var resolved = ResolveReported(source, state, reportedKeyToFindingId);
                if (resolved is not null && resolved != ownFindingId)
                    return resolved;
            }
        }

        return null;
    }

    /// <summary>Map a dead graph key to the finding id actually reported for it, walking to a reported ancestor.</summary>
    private static string? ResolveReported(
        string key, GraphState state, Dictionary<string, string> reportedKeyToFindingId)
    {
        if (reportedKeyToFindingId.TryGetValue(key, out var direct)) return direct;
        if (!state.Declared.TryGetValue(key, out var symbol)) return null;
        for (var container = symbol.ContainingType; container is not null; container = container.ContainingType)
            if (SymbolId.For(container) is { } cId && reportedKeyToFindingId.TryGetValue(cId, out var id))
                return id;
        return null;
    }

    /// <summary>
    /// Emit an <see cref="FindingKind.UnusedProjectReference"/> for each declared &lt;ProjectReference&gt;
    /// whose referencing project's code touches NO symbol in the referenced project's assembly.
    /// </summary>
    /// <remarks>
    /// CONSERVATIVE by design (invariant #8): a reference can be load-bearing with no symbol edges —
    /// transitive restore, runtime-only deps, [InternalsVisibleTo]. We only look at references between
    /// two C# projects that are BOTH in the analyzed set; anything else (non-C#, ignored, unresolved)
    /// is left alone. When unsure we prefer a false negative (don't flag) over a false positive.
    /// </remarks>
    private void BuildProjectReferenceFindings(
        List<Project> projects, GraphState state, AnalysisResult result)
    {
        // Only reason about references whose target is a project we actually analyzed; otherwise we
        // have no usage data for it and could flag a genuinely-used reference.
        var analyzed = projects.ToDictionary(p => p.Id, p => p);

        foreach (var project in projects)
        {
            var used = state.UsedAssemblies.TryGetValue(project.AssemblyName, out var set)
                ? set
                // ISet (not IReadOnlySet) is the common surface across net10.0 and net472 BCLs.
                : (ISet<string>)System.Collections.Immutable.ImmutableHashSet<string>.Empty;

            foreach (var reference in project.ProjectReferences)
            {
                if (!analyzed.TryGetValue(reference.ProjectId, out var referenced)) continue;

                // Self-reference (defensive) — never flag.
                if (string.Equals(referenced.AssemblyName, project.AssemblyName, StringComparison.Ordinal))
                    continue;

                if (used.Contains(referenced.AssemblyName)) continue; // reference IS exercised

                var csproj = project.FilePath ?? project.Name;
                // The deletion unit is the <ProjectReference/> element; `location` stays at 0/0 (the
                // finding points at the .csproj as a whole — the element position lives in `span`).
                var refSpan = ProjectFileSpan.ForProjectReference(csproj, referenced);
                result.Findings.Add(new Finding(
                    FindingKind.UnusedProjectReference,
                    referenced.Name,
                    "project reference",
                    "",
                    project.Name,
                    csproj,
                    0,
                    0,
                    referenced.Name)
                {
                    Id = FindingEnrichment.ComputeId(
                        FindingKind.UnusedProjectReference, referenced.Name, project.Name, referenced.Name),
                    Span = refSpan,
                    Remediation = Model.Remediation.RemoveProjectReference,
                });
            }
        }
    }

    /// <summary>
    /// Emit a <see cref="FindingKind.UnusedPackageReference"/> for each declared &lt;PackageReference&gt;
    /// none of whose delivered assemblies appears in the referencing project's EXTERNAL-assembly use set
    /// (<see cref="GraphState.UsedExternalAssemblies"/> — the non-solution edges the walker records before
    /// dropping them, invariant #5).
    /// </summary>
    /// <remarks>
    /// A declared package is graded against its full DEPENDENCY CLOSURE (the package plus every package it
    /// transitively pulls, per the assets <c>dependencies</c> graph), NOT its own assemblies alone. This is
    /// what keeps a used METAPACKAGE off the list: a package like <c>Swashbuckle.AspNetCore</c> declares an
    /// empty own <c>compile</c> set but its DEPENDENCY packages deliver the used assemblies
    /// (<c>…SwaggerGen.dll</c> etc.) — the closure sees those, so the reference is USED and neither flagged
    /// nor mis-tagged build-only.
    /// <para>Per REVISED §3.8 (recall over silence): known-hazard packages are EMITTED, not dropped:</para>
    /// <list type="bullet">
    ///   <item>analyzer / source-generator / build-only packages deliver NO referenceable compile
    ///     assembly ANYWHERE in their closure, so their effect (codegen, targets, roslyn analysis) is
    ///     invisible to symbol edges — tagged <see cref="Hazard.BuildOnlyPackage"/> and demoted to low
    ///     confidence. A metapackage that IS used is NOT build-only: a used assembly lives in its closure;</item>
    ///   <item><c>PrivateAssets="all"</c> references are build/dev-only by intent — same tag/tier;</item>
    ///   <item>a package used only via a TRANSITIVE type, or an implicit <c>Using</c>, still shows up as
    ///     unused when nothing in its closure is touched — emitted at C3 medium (package-ref) so the agent
    ///     triages it through the verify loop.</item>
    /// </list>
    /// Requires restore data: <c>obj/project.assets.json</c> (preferred) or resolved metadata-reference
    /// paths. When neither yields a package map, the project's package references are left alone (no
    /// restore data = no verdict) — conservative, and reported as an absence, never a false flag.
    /// </remarks>
    private void BuildPackageReferenceFindings(List<Project> projects, GraphState state, AnalysisResult result)
    {
        foreach (var project in projects)
        {
            var declared = PackageReferenceReader.Read(project.FilePath ?? "");
            if (declared.Count == 0) continue;

            var packageAssemblies = PackageAssemblyMap.Build(project);
            if (packageAssemblies.Count == 0) continue; // no restore data → no verdict (conservative)

            var used = state.UsedExternalAssemblies.TryGetValue(project.AssemblyName, out var set)
                ? set
                : (ISet<string>)System.Collections.Immutable.ImmutableHashSet<string>.Empty;

            foreach (var package in declared)
            {
                // No mapping for this id (e.g. an analyzer package absent from the metadata-ref fallback,
                // or a framework/meta package): we have no delivered-assembly evidence, so we cannot make
                // an honest verdict — leave it alone (conservative, invariant #8 safe direction).
                var closure = PackageAssemblyMap.Closure(packageAssemblies, package.Id);
                if (closure is null) continue;

                // Grade against the DEPENDENCY CLOSURE (package + transitive deps), so a METAPACKAGE whose
                // own compile set is empty but whose dependency packages deliver the used assemblies is
                // seen as USED — not flagged, not mis-tagged build-only. A package is exercised iff any
                // assembly in its closure is touched.
                var usedInClosure = closure.Assemblies.Any(a => used.Contains(a));
                if (usedInClosure) continue;

                // Build-only / analyzer / PrivateAssets: NO referenceable compile assembly ANYWHERE in the
                // closure OR explicitly dev-only. Its effect is invisible to symbol edges → EMIT with the
                // BuildOnlyPackage hazard + (via ConfidenceModel) low confidence. A used metapackage never
                // lands here (it was already continued above). Never dropped (REVISED §3.8).
                var buildOnly = !closure.DeliversCompileAssembly || package.PrivateAssetsAll;

                var csproj = project.FilePath ?? project.Name;
                result.Findings.Add(new Finding(
                    FindingKind.UnusedPackageReference,
                    package.Id,
                    "package reference",
                    "",
                    project.Name,
                    csproj,
                    0,
                    0)
                {
                    Id = FindingEnrichment.ComputeId(
                        FindingKind.UnusedPackageReference, package.Id, project.Name, null),
                    Span = package.Span,
                    Remediation = Model.Remediation.RemovePackageReference,
                    Hazards = buildOnly ? [Model.Hazard.BuildOnlyPackage] : [],
                });
            }
        }
    }

    private static void SortFindings(AnalysisResult result)
    {
        result.Findings.Sort((a, b) =>
        {
            var byProject = string.CompareOrdinal(a.Project, b.Project);
            if (byProject != 0) return byProject;
            var byFile = string.CompareOrdinal(a.FilePath, b.FilePath);
            if (byFile != 0) return byFile;
            var byLine = a.Line.CompareTo(b.Line);
            return byLine != 0 ? byLine : string.CompareOrdinal(a.Symbol, b.Symbol);
        });
    }

    private bool ShouldReport(string id, ISymbol symbol, HashSet<string> dead, GraphState state)
    {
        // H11: never report a declaration that lives in a BUILT-IN generated tree — walked for its edges,
        // but its own dead code is not the user's to delete. Mirrors the ignore-for-reporting path below
        // (in the graph, suppressed for reporting only).
        if (state.GeneratedDeclarations.Contains(id)) return false;

        // Report the outermost dead symbol only: if the containing type is dead, skip the member.
        for (var container = symbol.ContainingType; container is not null; container = container.ContainingType)
            if (SymbolId.For(container) is { } containerId && dead.Contains(containerId)) return false;

        if (symbol.IsImplicitlyDeclared) return false;
        if (symbol.Name.Length == 0 || symbol.Name[0] is '<' or '$') return false; // compiler-generated

        switch (symbol)
        {
            case IMethodSymbol method:
                // Constructors/finalizers are often invoked implicitly (DI, reflection, runtime).
                if (method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
                    or MethodKind.Destructor) return false;
                // Overrides and interface implementations are called via the base/interface; too noisy.
                if (method.IsOverride || ImplementsInterface(method)) return false;
                break;
            case IPropertySymbol property when property.IsOverride || ImplementsInterface(property):
                return false;
            case IEventSymbol @event when @event.IsOverride || ImplementsInterface(@event):
                return false;
        }

        if (!symbol.Locations.Any(l => l.IsInSource)) return false;
        return !IsIgnored(symbol);
    }

    private bool IsIgnored(ISymbol symbol)
    {
        // ignore.symbols matches a symbol by its FULLY-QUALIFIED name — the same shape shown in findings
        // (DisplayFormat): namespace + containing type(s) + member name, with parameters for methods
        // (e.g. "CatI.I2.Sample.OnlyDead()", "CatI.I2.Sample.IgnoredProperty", type "CatI.I2.Sample").
        // A doc-style glob like "CatI.I2.Sample.Ignored*" therefore matches the MEMBER, not only a bare
        // name. We deliberately reuse DisplayFormat (rather than FqFormat, whose member rendering is the
        // bare name and is shared with ignore.namespaces / entry-point base-type matching) so the ignore
        // name is consistent with what the user sees reported.
        if (Glob.IsMatchAny(symbol.ToDisplayString(DisplayFormat), _config.Ignore.Symbols)) return true;
        var ns = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return Glob.IsMatchAny(ns, _config.Ignore.Namespaces);
    }

    private static bool ImplementsInterface(ISymbol member)
    {
        var type = member.ContainingType;
        if (type is null) return false;
        foreach (var iface in type.AllInterfaces)
            foreach (var ifaceMember in iface.GetMembers())
            {
                var impl = type.FindImplementationForInterfaceMember(ifaceMember);
                if (impl is not null && SymbolEqualityComparer.Default.Equals(impl, member))
                    return true;
            }
        return false;
    }

    private static Finding? ToFinding(ISymbol symbol, HashSet<string> ivtToNonSolution)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null) return null;

        FindingKind? kind = symbol switch
        {
            INamedTypeSymbol => FindingKind.UnusedType,
            IMethodSymbol => FindingKind.UnusedMethod,
            IPropertySymbol => FindingKind.UnusedProperty,
            // An enum member is an IFieldSymbol whose containing type is an enum — report it as its own
            // kind (clearer remediation than a bare field) BEFORE the generic field case.
            IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => FindingKind.UnusedEnumMember,
            IFieldSymbol => FindingKind.UnusedField,
            IEventSymbol => FindingKind.UnusedEvent,
            _ => null,
        };
        if (kind is null) return null;

        var lineSpan = location.GetLineSpan();
        var displayName = symbol.ToDisplayString(DisplayFormat);
        var project = symbol.ContainingAssembly?.Name ?? "?";
        var hasIvt = symbol.ContainingAssembly is { } asm && ivtToNonSolution.Contains(asm.Name);
        return new Finding(
            kind.Value,
            displayName,
            SymbolKindName(symbol),
            AccessibilityName(symbol.DeclaredAccessibility),
            project,
            lineSpan.Path,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1)
        {
            Id = FindingEnrichment.ComputeId(kind.Value, displayName, project, null),
            Span = FindingEnrichment.ComputeSpan(symbol),
            Remediation = FindingEnrichment.RemediationFor(kind.Value),
            // WS8b-2: attach ADVISORY hazards (publicApi / internalsVisibleTo). Confidence is graded in a
            // final pass (ConfidenceModel.Apply) once the reliability picture is complete.
            Hazards = FindingEnrichment.ComputeHazards(symbol, hasIvt),
        };
    }

    /// <summary>
    /// (WS7) An OnlyUsedByTests finding for a production symbol reachable only via test roots. Same span /
    /// location / hazards as an ordinary finding, but a distinct kind + DeleteCodeAndTests remediation, and
    /// it carries the referencing test symbols (K3) so the deletion unit — code AND its tests — is visible.
    /// </summary>
    private static Finding? ToTestOnlyFinding(
        ISymbol symbol,
        HashSet<string> ivtToNonSolution,
        Dictionary<string, List<TestReferrer>> testReferrers)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null) return null;

        // OnlyUsedByTests reports whole symbols (types, methods, properties, fields, events, enum
        // members). Anything ToFinding wouldn't map (already filtered by ShouldReport) shouldn't reach here.
        var lineSpan = location.GetLineSpan();
        var displayName = symbol.ToDisplayString(DisplayFormat);
        var project = symbol.ContainingAssembly?.Name ?? "?";
        var hasIvt = symbol.ContainingAssembly is { } asm && ivtToNonSolution.Contains(asm.Name);

        var id = SymbolId.For(symbol);
        var referrers = id is not null && testReferrers.TryGetValue(id, out var list)
            ? (IReadOnlyList<TestReferrer>)list
            : [];

        return new Finding(
            FindingKind.OnlyUsedByTests,
            displayName,
            SymbolKindName(symbol),
            AccessibilityName(symbol.DeclaredAccessibility),
            project,
            lineSpan.Path,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1)
        {
            Id = FindingEnrichment.ComputeId(FindingKind.OnlyUsedByTests, displayName, project, null),
            Span = FindingEnrichment.ComputeSpan(symbol),
            Remediation = Model.Remediation.DeleteCodeAndTests,
            Hazards = FindingEnrichment.ComputeHazards(symbol, hasIvt),
            TestReferrers = referrers,
        };
    }

    private static string SymbolKindName(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { IsRecord: true } t => t.TypeKind == TypeKind.Struct ? "record struct" : "record",
        INamedTypeSymbol t => t.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => "type",
        },
        IMethodSymbol => "method",
        IPropertySymbol { IsIndexer: true } => "indexer",
        IPropertySymbol => "property",
        IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => "enum member",
        IFieldSymbol { IsConst: true } => "const",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        _ => symbol.Kind.ToString().ToLowerInvariant(),
    };

    private static string AccessibilityName(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "",
    };
}
