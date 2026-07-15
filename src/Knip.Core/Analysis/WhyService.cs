using System.Text;
using Knip.Core.Model;
using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>
/// (WS8c) The retained reachability graph a <c>--why</c> run keeps alive so provenance can be rendered
/// AFTER analysis. Held on <see cref="AnalysisResult.WhyContext"/> as an opaque <c>object</c> so no
/// internal graph type or graph key (invariant #1) crosses the public API. Only produced when the caller
/// asks for provenance (memory gated).
/// </summary>
internal sealed class WhyContext(GraphState state, HashSet<string> reachable)
{
    public GraphState State { get; } = state;
    public HashSet<string> Reachable { get; } = reachable;
}

/// <summary>
/// (WS8c) Renders the human <c>--why</c> report for a symbol: why a flagged symbol is dead (its incoming
/// dead referrers, or "no incoming references"), or — for a live symbol — the SHORTEST root→symbol path.
/// Output is prose + display names + <c>file:line</c> ONLY; a raw graph key (<c>Assembly::docId</c>) is
/// NEVER printed (invariant #1). The argument resolves to a symbol by finding <c>id</c> (<c>k1_…</c>) or
/// by display name (exact, else unambiguous suffix).
/// </summary>
public static class WhyService
{
    private static readonly SymbolDisplayFormat DisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>
    /// Produce the report for <paramref name="query"/> against <paramref name="result"/>. Returns a
    /// human string (never contains a graph key). When the query can't be resolved, returns a "not found"
    /// message listing near matches. Requires the run to have been executed with provenance capture; if
    /// it wasn't, returns a message saying so (the CLI always captures for <c>--why</c>).
    /// </summary>
    public static string Explain(AnalysisResult result, string query)
    {
        if (result.WhyContext is not WhyContext ctx)
            return "error: --why requires provenance capture (internal: run was not started with it).";

        var state = ctx.State;

        // 1) Resolve the query to a graph key. Prefer a finding id match (stable, unambiguous), then an
        //    exact display-name match, then an unambiguous suffix. Keys stay internal throughout.
        if (!TryResolve(query, result, state, out var key, out var resolutionError))
            return resolutionError!;

        var symbol = state.Declared[key];
        var display = symbol.ToDisplayString(DisplayFormat);
        var where = LocationOf(symbol);

        var sb = new StringBuilder();
        sb.Append("why: ").Append(display);
        if (where is not null) sb.Append(" (").Append(where).Append(')');
        sb.AppendLine();

        if (ctx.Reachable.Contains(key))
            AppendAlive(sb, state, ctx.Reachable, key, display);
        else
            AppendFlagged(sb, result, state, key, display);

        return sb.ToString().TrimEnd();
    }

    // ── flagged (dead) symbol: why is it unreachable? ────────────────────────────────────────────
    private static void AppendFlagged(
        StringBuilder sb, AnalysisResult result, GraphState state, string key, string display)
    {
        sb.AppendLine("status: FLAGGED (unreachable from any root)");

        // Incoming edges over the WHOLE graph — every source that references this symbol. If ANY exists
        // they are all dead (a live source would have kept the symbol alive), so we report them as
        // "referenced only by <dead symbols>". Otherwise it is directly unreferenced.
        var referrers = new List<string>();
        foreach (var (source, targets) in state.Edges)
        {
            if (source == key || !targets.Contains(key)) continue;
            if (!state.Declared.TryGetValue(source, out var srcSymbol)) continue;
            referrers.Add(RenderHop(srcSymbol, EdgeSiteOrDecl(state, source, key, srcSymbol)));
        }

        if (referrers.Count == 0)
        {
            sb.AppendLine("  no incoming references (directly unreferenced)");
        }
        else
        {
            sb.AppendLine("  referenced only by (all dead — dead code confers no life):");
            foreach (var referrer in referrers.OrderBy(r => r, StringComparer.Ordinal))
                sb.Append("    ").AppendLine(referrer);
        }

        // rootCause (L10), when this symbol's finding carries one, points at the nearest dead symbol
        // keeping it dead — surface it as a display-name hint. Match the finding by display name.
        var finding = result.Findings.FirstOrDefault(f => string.Equals(f.Symbol, display, StringComparison.Ordinal));
        if (finding?.RootCause is { } rootCauseId
            && result.Findings.FirstOrDefault(f => f.Id == rootCauseId) is { } cause)
            sb.Append("  root cause: ").Append(cause.Symbol).AppendLine(" (delete it first)");
    }

    // ── alive symbol: the shortest root→symbol path (BFS parent-tracking from roots) ─────────────
    private static void AppendAlive(
        StringBuilder sb, GraphState state, HashSet<string> reachable, string key, string display)
    {
        sb.AppendLine("status: ALIVE (reachable from a root)");

        var path = ShortestRootPath(state, reachable, key);
        if (path is null || path.Count == 0)
        {
            // The symbol is itself a root (no path needed).
            sb.AppendLine("  it is a ROOT (entry point / framework-invoked / public API surface)");
            return;
        }

        sb.Append("  ");
        for (var i = 0; i < path.Count; i++)
        {
            if (i > 0) sb.Append(" → ");
            var id = path[i];
            var symbol = state.Declared[id];
            // The hop's site is the reference from the PREVIOUS hop into this one (file:line of the use).
            var prev = i > 0 ? path[i - 1] : null;
            var site = prev is null ? LocationOf(symbol) : EdgeSiteOrDecl(state, prev, id, symbol);
            sb.Append(RenderHop(symbol, site));
        }
        sb.AppendLine();
    }

    /// <summary>BFS from all roots, tracking parents, to the shortest root→<paramref name="target"/> path.</summary>
    private static List<string>? ShortestRootPath(GraphState state, HashSet<string> reachable, string target)
    {
        if (state.Roots.Contains(target)) return [];

        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        // Deterministic seed order.
        foreach (var root in state.Roots.Where(reachable.Contains).OrderBy(r => r, StringComparer.Ordinal))
        {
            if (parent.ContainsKey(root)) continue;
            parent[root] = root; // self-parent marks a root
            queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!state.Edges.TryGetValue(current, out var targets)) continue;
            foreach (var next in targets.OrderBy(t => t, StringComparer.Ordinal))
            {
                if (!state.Declared.ContainsKey(next) || parent.ContainsKey(next)) continue;
                parent[next] = current;
                if (next == target)
                    return BuildPath(parent, target);
                queue.Enqueue(next);
            }
        }

        return parent.ContainsKey(target) ? BuildPath(parent, target) : null;
    }

    private static List<string> BuildPath(Dictionary<string, string> parent, string target)
    {
        var path = new List<string> { target };
        var current = target;
        while (parent[current] is var p && !string.Equals(p, current, StringComparison.Ordinal))
        {
            path.Add(p);
            current = p;
        }
        path.Reverse();
        return path;
    }

    // ── resolution ───────────────────────────────────────────────────────────────────────────────
    private static bool TryResolve(
        string query, AnalysisResult result, GraphState state, out string key, out string? error)
    {
        key = "";
        error = null;
        query = query.Trim();

        // (a) a finding id (k1_…): map it back to the reported symbol's graph key via its display name.
        if (query.StartsWith("k1_", StringComparison.Ordinal))
        {
            var finding = result.Findings.FirstOrDefault(f => f.Id == query);
            if (finding is null)
            {
                error = $"why: no finding with id '{query}'.";
                return false;
            }
            // A project/package-reference finding has no symbol node.
            if (finding.Kind is FindingKind.UnusedProjectReference or FindingKind.UnusedPackageReference)
            {
                error = $"why: '{finding.Symbol}' is a {finding.SymbolKind}, not a symbol (nothing to trace).";
                return false;
            }
            if (TryKeyByDisplay(finding.Symbol, state, out key)) return true;
            error = $"why: could not locate the symbol for finding '{query}' ({finding.Symbol}).";
            return false;
        }

        // (b) exact display-name match.
        if (TryKeyByDisplay(query, state, out key)) return true;

        // (c) unambiguous suffix match (e.g. "Foo.Bar()" or "Bar()").
        var matches = state.Declared
            .Where(kv => kv.Value.ToDisplayString(DisplayFormat)
                .EndsWith(query, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToList();

        if (matches.Count == 1)
        {
            key = matches[0];
            return true;
        }

        if (matches.Count == 0)
        {
            error = $"why: no symbol matches '{query}'. Pass a finding id (k1_…) or a display name.";
            return false;
        }

        var names = matches
            .Select(k => state.Declared[k].ToDisplayString(DisplayFormat))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Take(10);
        error = $"why: '{query}' is ambiguous — matches:\n" +
            string.Join("\n", names.Select(n => "  " + n)) +
            "\nQualify the name or pass the finding id.";
        return false;
    }

    private static bool TryKeyByDisplay(string display, GraphState state, out string key)
    {
        foreach (var (k, symbol) in state.Declared)
            if (string.Equals(symbol.ToDisplayString(DisplayFormat), display, StringComparison.Ordinal))
            {
                key = k;
                return true;
            }
        key = "";
        return false;
    }

    // ── rendering helpers (display names + file:line only — never a graph key) ────────────────────
    private static string RenderHop(ISymbol symbol, string? site)
    {
        var name = symbol.ToDisplayString(DisplayFormat);
        return site is null ? name : $"{name} ({site})";
    }

    /// <summary>The edge's recorded reference SITE (file:line), else the target symbol's declaration.</summary>
    private static string? EdgeSiteOrDecl(GraphState state, string source, string target, ISymbol targetSymbol)
    {
        var edgeKey = source + "" + target;
        if (state.EdgeSources.TryGetValue(edgeKey, out var location))
            return Format(location);
        return LocationOf(targetSymbol);
    }

    private static string? LocationOf(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        return location is null ? null : Format(location);
    }

    private static string Format(Location location)
    {
        var span = location.GetLineSpan();
        return $"{Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1}";
    }
}
