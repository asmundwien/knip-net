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

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Analyzing {project.Name}");
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;
            result.ProjectsAnalyzed++;

            var isPublicApi = Glob.IsMatchAny(project.Name, _config.Roots.PublicApiProjects);
            if (compilation.GetEntryPoint(ct) is { } entry)
            {
                if (SymbolId.For(entry) is { } entryId) state.Roots.Add(entryId);
                if (entry.ContainingType is { } host && SymbolId.For(host) is { } hostId) state.Roots.Add(hostId);
            }

            foreach (var tree in compilation.SyntaxTrees)
            {
                ct.ThrowIfCancellationRequested();
                if (Glob.IsMatchAny(tree.FilePath, _config.Ignore.Files)) continue;
                var model = compilation.GetSemanticModel(tree);
                var walker = new ReferenceWalker(model, _config, isPublicApi, solutionAssemblies, state);
                walker.Visit(await tree.GetRootAsync(ct));
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
        BuildFindings(state, reachable, result);
        BuildProjectReferenceFindings(solution, projects, state, result);

        if (state.UnresolvedTypeReferences > 0)
            result.LoadDiagnostics.Insert(0,
                $"{state.UnresolvedTypeReferences} reference(s) to unresolved types — the solution may not be " +
                "fully restored/buildable. Run 'dotnet restore' (and ensure private feeds are authenticated); " +
                "otherwise dead-code results can include false positives.");

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

    private void BuildFindings(GraphState state, HashSet<string> reachable, AnalysisResult result)
    {
        var dead = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, _) in state.Declared)
            if (!reachable.Contains(id)) dead.Add(id);

        foreach (var (id, symbol) in state.Declared)
        {
            if (!dead.Contains(id)) continue;
            if (!ShouldReport(symbol, dead)) continue;
            if (ToFinding(symbol) is { } finding) result.Findings.Add(finding);
        }
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

                result.Findings.Add(new Finding(
                    FindingKind.UnusedProjectReference,
                    referenced.Name,
                    "project reference",
                    "",
                    project.Name,
                    project.FilePath ?? project.Name,
                    0,
                    0,
                    referenced.Name));
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

    private bool ShouldReport(ISymbol symbol, HashSet<string> dead)
    {
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

    private static Finding? ToFinding(ISymbol symbol)
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

        var span = location.GetLineSpan();
        return new Finding(
            kind.Value,
            symbol.ToDisplayString(DisplayFormat),
            SymbolKindName(symbol),
            AccessibilityName(symbol.DeclaredAccessibility),
            symbol.ContainingAssembly?.Name ?? "?",
            span.Path,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1);
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
