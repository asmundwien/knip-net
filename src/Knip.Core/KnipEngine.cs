using Knip.Core.Analysis;
using Knip.Core.Configuration;
using Knip.Core.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Knip.Core;

/// <summary>
/// Entry point for a Knip.NET run: opens the solution/project via MSBuild and runs the analyzer.
/// The caller must have registered an MSBuild instance (via MSBuildLocator) before calling this.
/// </summary>
public static class KnipEngine
{
    public static async Task<AnalysisResult> RunAsync(
        KnipConfig config,
        string targetPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        using var workspace = MSBuildWorkspace.Create();
        workspace.SkipUnrecognizedProjects = true;

        var loadDiagnostics = new List<string>();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                loadDiagnostics.Add(e.Diagnostic.Message);
        };

        progress?.Report($"Loading {Path.GetFileName(targetPath)}");

        Solution solution = IsProjectFile(targetPath)
            ? (await workspace.OpenProjectAsync(targetPath, cancellationToken: ct)).Solution
            : await workspace.OpenSolutionAsync(targetPath, cancellationToken: ct);

        var analyzer = new DeadCodeAnalyzer(config);
        var result = await analyzer.AnalyzeAsync(solution, progress, ct);
        result.LoadDiagnostics.AddRange(loadDiagnostics);
        return result;
    }

    private static bool IsProjectFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
}
