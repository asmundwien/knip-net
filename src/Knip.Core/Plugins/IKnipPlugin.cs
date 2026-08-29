using Knip.Core.Configuration;
using Microsoft.CodeAnalysis;

namespace Knip.Core.Plugins;

/// <summary>
/// A plugin contributes EXTRA roots and EXTRA edges for usages the core walker cannot see
/// (reflection, scanning DI, serialization, markup binding, …). It may only ADD reachability;
/// it can never remove a node, edge, or root (invariant #8, RUNBOOK.md).
/// </summary>
public interface IKnipPlugin
{
    /// <summary>
    /// Stable, camelCase id used in config to enable/disable this plugin, e.g. "reflection",
    /// "scanningDi". Must match the key under <c>plugins</c> in knip.json.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Inspect one project's compilation and contribute roots/edges via the sink on
    /// <paramref name="context"/>. Runs once per C# project, AFTER the core walk of that project,
    /// BEFORE global traversal. Called even for plugins that end up contributing nothing.
    /// </summary>
    void Contribute(PluginContext context, CancellationToken ct);
}

/// <summary>Everything a plugin is allowed to look at and do. One project's world.</summary>
public sealed class PluginContext
{
    internal PluginContext(Compilation compilation, Project project, PluginSettings settings, IContributionSink sink)
    {
        Compilation = compilation;
        Project = project;
        Settings = settings;
        Sink = sink;
    }

    /// <summary>The current project's compilation. Get semantic models from this as needed.</summary>
    public Compilation Compilation { get; }

    /// <summary>The project being analyzed (name, file path — for diagnostics / project-scoped rules).</summary>
    public Project Project { get; }

    /// <summary>Read-only view of the plugin's own config block from knip.json. Never null.</summary>
    public PluginSettings Settings { get; }

    /// <summary>The ONLY way to mutate the graph. Symbol-typed, additive-only.</summary>
    public IContributionSink Sink { get; }
}

/// <summary>
/// The ONLY way a plugin mutates the graph. Symbol-typed on the way in; the sink derives the
/// SymbolId key, applies the solution-assembly filter, and records the current project's test or production
/// origin internally. Every method is additive — there is no remove/suppress verb.
/// </summary>
public interface IContributionSink
{
    /// <summary>
    /// Mark <paramref name="symbol"/> as a root (reachability starts here). No-op if it has no
    /// SymbolId (dynamic/error symbols) or is not solution-defined.
    /// </summary>
    void AddRoot(ISymbol symbol);

    /// <summary>
    /// Record a "uses" edge <paramref name="from"/> → <paramref name="to"/>. No-op if either end
    /// lacks a SymbolId or <paramref name="to"/> is not solution-defined (mirrors ReferenceWalker.AddEdge).
    /// </summary>
    void AddEdge(ISymbol from, ISymbol to);
}
