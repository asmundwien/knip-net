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

    public async Task<AnalysisResult> AnalyzeAsync(Solution solution, IProgress<string>? progress, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var state = new GraphState();
        var result = new AnalysisResult();

        var projects = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp && !Glob.IsMatchAny(p.Name, _config.Ignore.Projects))
            .ToList();

        // Assemblies whose symbols count as "in the solution" — edges to anything else (BCL, NuGet)
        // are dropped. Precomputed so cross-project edges resolve regardless of analysis order.
        var solutionAssemblies = projects
            .Select(p => p.AssemblyName)
            .ToHashSet(StringComparer.Ordinal);

        // Config typos (unknown plugin id / unknown per-plugin key) surface as VISIBLE warnings rather
        // than silently no-opping. Prepended so they lead the diagnostics list.
        foreach (var warning in _config.ValidatePlugins())
            result.LoadDiagnostics.Add(warning);

        // The single choke point through which every plugin mutates the graph (invariants #1, #5, add-only).
        var sink = new ContributionSink(state, solutionAssemblies);

        // Per-DEFINING-assembly: does that project carry [InternalsVisibleTo] a NON-solution assembly?
        // Keyed by assembly name (invariant #1); drives the InternalsVisibleTo hazard in ToFinding.
        var ivtToNonSolution = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Analyzing {project.Name}");
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;
            result.ProjectsAnalyzed++;

            if (FindingEnrichment.HasInternalsVisibleToNonSolutionAssembly(compilation, solutionAssemblies))
                ivtToNonSolution.Add(compilation.Assembly.Name);

            var isPublicApi = Glob.IsMatchAny(project.Name, _config.Roots.PublicApiProjects);
            if (compilation.GetEntryPoint(ct) is { } entry)
            {
                if (SymbolId.For(entry) is { } entryId) state.Roots.Add(entryId);
                if (entry.ContainingType is { } host && SymbolId.For(host) is { } hostId) state.Roots.Add(hostId);
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
                    model, _config, isPublicApi, solutionAssemblies, state, generatedTree: isGenerated);
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

        var reachable = Traverse(state);
        BuildFindings(state, reachable, result, ivtToNonSolution);
        BuildProjectReferenceFindings(solution, projects, state, result);

        if (state.UnresolvedTypeReferences > 0)
            result.LoadDiagnostics.Insert(0,
                $"{state.UnresolvedTypeReferences} reference(s) to unresolved types — the solution may not be " +
                "fully restored/buildable. Run 'dotnet restore' (and ensure private feeds are authenticated); " +
                "otherwise dead-code results can include false positives.");

        // Reliability block (WS8 §1.1): what the analyzer knows. Project-load/restore failures observed at
        // the workspace boundary are attributed by KnipEngine after this returns.
        result.Reliability.ProjectsLoaded = result.ProjectsAnalyzed;
        result.Reliability.UnresolvedTypeReferences = state.UnresolvedTypeReferences;

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

    private static HashSet<string> Traverse(GraphState state)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        foreach (var root in state.Roots)
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
        GraphState state, HashSet<string> reachable, AnalysisResult result, HashSet<string> ivtToNonSolution)
    {
        var dead = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, _) in state.Declared)
            if (!reachable.Contains(id)) dead.Add(id);

        // Graph key of each REPORTED dead symbol → its emitted finding id. A dead member whose containing
        // type is also dead is NOT reported (the outermost type is); rootCause resolution walks up to the
        // reported ancestor via this map. Two passes: emit findings first, then attribute rootCause.
        var reportedKeyToFindingId = new Dictionary<string, string>(StringComparer.Ordinal);
        var emitted = new List<(string Key, ISymbol Symbol, int Index)>();

        foreach (var (id, symbol) in state.Declared)
        {
            if (!dead.Contains(id)) continue;
            if (!ShouldReport(id, symbol, dead, state)) continue;
            if (ToFinding(symbol, ivtToNonSolution) is not { } finding) continue;
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
        Solution solution, List<Project> projects, GraphState state, AnalysisResult result)
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
        if (Glob.IsMatchAny(symbol.ToDisplayString(ReferenceWalker.FqFormat), _config.Ignore.Symbols)) return true;
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
