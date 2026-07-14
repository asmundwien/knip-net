using Knip.Core;
using Knip.Core.Configuration;
using Knip.Core.Model;

namespace Knip.Core.Tests;

/// <summary>
/// Resolves a fixture solution by path and runs <see cref="KnipEngine.RunAsync"/> against it,
/// returning the finding set as comparable display-name strings. Fixtures are NEVER project-
/// referenced: they are synthetic solutions the engine compiles via MSBuildWorkspace at runtime.
/// </summary>
public static class FixtureRunner
{
    /// <summary>Runs Knip on one fixture category solution with default config.</summary>
    public static async Task<AnalysisResult> RunAsync(string category, KnipConfig? config = null)
    {
        var solutionPath = ResolveFixtureSolution(category);
        return await KnipEngine.RunAsync(config ?? new KnipConfig(), solutionPath);
    }

    /// <summary>
    /// The exact set of reported symbols, as their display names (DeadCodeAnalyzer's DisplayFormat:
    /// namespace-qualified, parameters included). Assert on this for both what IS and IS NOT flagged.
    /// </summary>
    public static async Task<IReadOnlySet<string>> FindingSymbolsAsync(string category, KnipConfig? config = null)
    {
        var result = await RunAsync(category, config);
        return result.Findings.Select(f => f.Symbol).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The reported symbols scoped to a single scenario namespace (e.g. "CatE.E01").</summary>
    public static async Task<IReadOnlySet<string>> FindingSymbolsInAsync(
        string category, string scenarioNamespace, KnipConfig? config = null)
    {
        var all = await FindingSymbolsAsync(category, config);
        var prefix = scenarioNamespace + ".";
        return all.Where(s => s.StartsWith(prefix, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Finds tests/fixtures/&lt;category&gt;/Fixture.sln by walking up from the test assembly to the
    /// repo root (marked by Knip.slnx). Keeps fixtures out of bin/obj — they are compiled in place.
    /// </summary>
    public static string ResolveFixtureSolution(string category)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Knip.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new DirectoryNotFoundException(
                "Could not locate repo root (Knip.slnx) from " + AppContext.BaseDirectory);

        var solution = Path.Combine(dir.FullName, "tests", "fixtures", category, "Fixture.slnx");
        if (!File.Exists(solution))
            throw new FileNotFoundException("Fixture solution not found", solution);

        return solution;
    }
}
