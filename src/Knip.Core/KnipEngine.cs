using Knip.Core.Analysis;
using Knip.Core.Configuration;
using Knip.Core.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Knip.Core;

/// <summary>
/// Opens a target through MSBuild and returns either an analysis result or one typed symbol explanation.
/// The caller must register MSBuild before invoking this module.
/// </summary>
public static class KnipEngine
{
    public static Task<AnalysisResult> RunAsync(
        KnipConfig config,
        string targetPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        RunCoreAsync(config, targetPath, progress, ct, captureExplanation: false);

    public static async Task<ExplanationResult> ExplainAsync(
        KnipConfig config,
        string targetPath,
        string symbol,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = await RunCoreAsync(config, targetPath, progress, ct, captureExplanation: true);
        return ExplanationService.Explain(result, symbol);
    }

    private static async Task<AnalysisResult> RunCoreAsync(
        KnipConfig config,
        string targetPath,
        IProgress<string>? progress,
        CancellationToken ct,
        bool captureExplanation)
    {
        using var workspace = MSBuildWorkspace.Create();
        workspace.SkipUnrecognizedProjects = true;

        var workspaceDiagnostics = new List<string>();
        workspace.WorkspaceFailed += (_, eventArgs) =>
        {
            if (eventArgs.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                workspaceDiagnostics.Add(eventArgs.Diagnostic.Message);
        };

        progress?.Report($"Loading {Path.GetFileName(targetPath)}");
        Solution solution = IsProjectFile(targetPath)
            ? (await workspace.OpenProjectAsync(targetPath, cancellationToken: ct)).Solution
            : await workspace.OpenSolutionAsync(targetPath, cancellationToken: ct);

        var analyzer = new DeadCodeAnalyzer(config);
        var result = await analyzer.AnalyzeAsync(solution, progress, ct, captureExplanation);

        foreach (var message in workspaceDiagnostics)
        {
            var benign = WorkspaceDiagnosticClassifier.IsBenignNuGetAdvisory(message);
            if (!benign)
                result.Reliability.RestoreFailures.Add(message);
            result.Reliability.LoadDiagnostics.Add(new LoadDiagnostic(
                benign ? LoadSeverity.Warning : LoadSeverity.Error,
                benign ? "nugetAdvisory" : "loadFailure",
                message));
        }

        ConfidenceModel.Apply(result, config);
        return result;
    }

    private static bool IsProjectFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
}
