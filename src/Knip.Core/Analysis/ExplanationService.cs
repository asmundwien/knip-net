using Knip.Core.Model;
using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

public enum ExplanationStatus
{
    Alive,
    Unreachable,
    OnlyUsedByTests,
}

public sealed record ExplanationLocation(string File, int Line, int Column);

public sealed record ExplanationHop(string Symbol, ExplanationLocation? Location);

public sealed record ExplanationResult(
    Reliability Reliability,
    ExplanationStatus? Status,
    string? Symbol,
    string? FindingId,
    ExplanationLocation? Location,
    IReadOnlyList<ExplanationHop> Path,
    IReadOnlyList<ExplanationHop> Referrers,
    string? RootCauseId,
    string? RootCauseSymbol,
    string? Error,
    IReadOnlyList<string> NearbyNames);

internal sealed class ExplanationContext(GraphState state, HashSet<string> reachable)
{
    public GraphState State { get; } = state;
    public HashSet<string> Reachable { get; } = reachable;
}

internal static class ExplanationService
{
    private static readonly SymbolDisplayFormat DisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static ExplanationResult Explain(AnalysisResult result, string query)
    {
        if (result.ExplanationContext is not ExplanationContext context)
            return Failure(result, "explanation graph was not captured", []);

        var resolution = Resolve(query.Trim(), result, context.State);
        if (resolution.Key is null)
            return Failure(result, resolution.Error!, resolution.NearbyNames);

        var key = resolution.Key;
        var symbol = context.State.Declared[key];
        var display = Display(symbol);
        var finding = result.Findings.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, resolution.FindingId, StringComparison.Ordinal))
            ?? result.Findings.FirstOrDefault(candidate =>
                string.Equals(candidate.Symbol, display, StringComparison.Ordinal));

        var status = finding?.Kind == FindingKind.OnlyUsedByTests
            ? ExplanationStatus.OnlyUsedByTests
            : context.Reachable.Contains(key)
                ? ExplanationStatus.Alive
                : ExplanationStatus.Unreachable;

        var path = status switch
        {
            ExplanationStatus.Alive => BuildPath(
                context.State, context.State.Roots, key),
            ExplanationStatus.OnlyUsedByTests => BuildPath(
                context.State,
                context.State.Roots.Where(context.State.TestRoots.Contains),
                key),
            _ => [],
        };

        var referrers = status == ExplanationStatus.Unreachable
            ? FindReferrers(context.State, key)
            : [];
        var rootCause = finding?.RootCause is null
            ? null
            : result.Findings.FirstOrDefault(candidate => candidate.Id == finding.RootCause);

        return new ExplanationResult(
            result.Reliability,
            status,
            display,
            finding?.Id,
            LocationOf(symbol),
            path,
            referrers,
            finding?.RootCause,
            rootCause?.Symbol,
            null,
            []);
    }

    private static ExplanationResult Failure(
        AnalysisResult result, string error, IReadOnlyList<string> nearbyNames) => new(
            result.Reliability,
            null,
            null,
            null,
            null,
            [],
            [],
            null,
            null,
            error,
            nearbyNames);

    private static (string? Key, string? FindingId, string? Error, IReadOnlyList<string> NearbyNames) Resolve(
        string query, AnalysisResult result, GraphState state)
    {
        if (query.StartsWith("k1_", StringComparison.Ordinal))
        {
            var finding = result.Findings.FirstOrDefault(candidate => candidate.Id == query);
            if (finding is null)
                return (null, null, $"no finding with id '{query}'", Nearby(query, state));
            if (finding.Kind is FindingKind.UnusedProjectReference or FindingKind.UnusedPackageReference)
                return (null, null, $"finding '{query}' is a {finding.SymbolKind}, not a graph symbol", []);

            var findingKey = FindKeyForFinding(finding, state);
            return findingKey is null
                ? (null, null, $"could not locate the graph symbol for finding '{query}'", [])
                : (findingKey, finding.Id, null, []);
        }

        var matches = state.Declared
            .Where(pair => string.Equals(Display(pair.Value), query, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        return matches.Count switch
        {
            1 => (matches[0], null, null, []),
            > 1 => (null, null, $"symbol '{query}' is ambiguous", matches
                .Select(key => FormatCandidate(state.Declared[key])).Take(10).ToList()),
            _ => (null, null, $"no symbol matches '{query}'", Nearby(query, state)),
        };
    }

    private static string? FindKeyForFinding(Finding finding, GraphState state)
    {
        foreach (var (key, symbol) in state.Declared)
        {
            if (!string.Equals(Display(symbol), finding.Symbol, StringComparison.Ordinal))
                continue;
            var location = LocationOf(symbol);
            if (location is not null
                && string.Equals(Path.GetFullPath(location.File), Path.GetFullPath(finding.FilePath), StringComparison.OrdinalIgnoreCase)
                && location.Line == finding.Line)
                return key;
        }

        return state.Declared.FirstOrDefault(pair =>
            string.Equals(Display(pair.Value), finding.Symbol, StringComparison.Ordinal)).Key;
    }

    private static IReadOnlyList<string> Nearby(string query, GraphState state) => state.Declared.Values
        .Select(Display)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => NearbyRank(name, query))
        .ThenBy(name => name, StringComparer.Ordinal)
        .Take(10)
        .ToList();

    private static int NearbyRank(string name, string query)
    {
        if (name.StartsWith(query, StringComparison.Ordinal)) return 0;
        if (name.Contains(query, StringComparison.Ordinal)) return 1;
        return 2;
    }

    private static IReadOnlyList<ExplanationHop> FindReferrers(GraphState state, string target)
    {
        var referrers = new List<ExplanationHop>();
        foreach (var (source, targets) in state.Edges)
        {
            if (source == target || !targets.Contains(target) || !state.Declared.TryGetValue(source, out var symbol))
                continue;
            referrers.Add(new ExplanationHop(Display(symbol), EdgeSiteOrDeclaration(state, source, target, symbol)));
        }
        return referrers.OrderBy(item => item.Symbol, StringComparer.Ordinal)
            .ThenBy(item => item.Location?.File, StringComparer.Ordinal)
            .ThenBy(item => item.Location?.Line)
            .ToList();
    }

    private static IReadOnlyList<ExplanationHop> BuildPath(
        GraphState state, IEnumerable<string> roots, string target)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var root in roots.OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!parent.TryAdd(root, root)) continue;
            queue.Enqueue(root);
        }

        while (queue.Count > 0 && !parent.ContainsKey(target))
        {
            var current = queue.Dequeue();
            if (!state.Edges.TryGetValue(current, out var targets)) continue;
            foreach (var next in targets.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (!state.Declared.ContainsKey(next) || !parent.TryAdd(next, current)) continue;
                queue.Enqueue(next);
            }
        }

        if (!parent.ContainsKey(target)) return [];
        var keys = new List<string> { target };
        while (parent[keys[^1]] is var previous && previous != keys[^1])
            keys.Add(previous);
        keys.Reverse();

        var path = new List<ExplanationHop>(keys.Count);
        for (var index = 0; index < keys.Count; index++)
        {
            var key = keys[index];
            var symbol = state.Declared[key];
            var location = index == 0
                ? LocationOf(symbol)
                : EdgeSiteOrDeclaration(state, keys[index - 1], key, symbol);
            path.Add(new ExplanationHop(Display(symbol), location));
        }
        return path;
    }

    private static ExplanationLocation? EdgeSiteOrDeclaration(
        GraphState state, string source, string target, ISymbol targetSymbol)
    {
        var edgeKey = source + "\u001f" + target;
        return state.EdgeSources.TryGetValue(edgeKey, out var location)
            ? ToLocation(location)
            : LocationOf(targetSymbol);
    }

    private static string FormatCandidate(ISymbol symbol)
    {
        var location = LocationOf(symbol);
        return location is null
            ? Display(symbol)
            : $"{Display(symbol)} ({location.File}:{location.Line}:{location.Column})";
    }

    private static string Display(ISymbol symbol) => symbol.ToDisplayString(DisplayFormat);

    private static ExplanationLocation? LocationOf(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        return location is null ? null : ToLocation(location);
    }

    private static ExplanationLocation ToLocation(Location location)
    {
        var span = location.GetLineSpan();
        return new ExplanationLocation(
            Path.GetFullPath(span.Path),
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1);
    }
}
