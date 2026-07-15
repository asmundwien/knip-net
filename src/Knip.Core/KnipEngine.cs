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

        var loadDiagnostics = new List<(WorkspaceDiagnosticKind Kind, string Message)>();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                loadDiagnostics.Add((e.Diagnostic.Kind, e.Diagnostic.Message));
        };

        progress?.Report($"Loading {Path.GetFileName(targetPath)}");

        Solution solution = IsProjectFile(targetPath)
            ? (await workspace.OpenProjectAsync(targetPath, cancellationToken: ct)).Solution
            : await workspace.OpenSolutionAsync(targetPath, cancellationToken: ct);

        var analyzer = new DeadCodeAnalyzer(config);
        var result = await analyzer.AnalyzeAsync(solution, progress, ct);

        foreach (var (_, message) in loadDiagnostics)
        {
            result.LoadDiagnostics.Add(message);
            // Workspace failures are genuine load failures — restore/SDK/project problems (invariant #6:
            // stay LOUD). Attributed into the reliability block as restore failures; degraded => true.
            result.Reliability.RestoreFailures.Add(message);
        }

        BuildStructuredDiagnostics(result);

        // WS8b-2 L9: grade confidence in a FINAL pass, once reliability (incl. workspace restore/load
        // failures attributed above) is complete — C1 per-project attribution needs the full picture.
        // Hazards were attached by the analyzer; this only demotes confidence off them + reliability.
        Analysis.ConfidenceModel.Apply(result, config);
        return result;
    }

    /// <summary>
    /// Mirror the human-readable <see cref="AnalysisResult.LoadDiagnostics"/> into the structured
    /// reliability channel (WS8 §1.1). Workspace/restore failures are error-severity (they drive
    /// <see cref="Reliability.Degraded"/>); everything else is a warning.
    /// </summary>
    private static void BuildStructuredDiagnostics(AnalysisResult result)
    {
        var restoreFailures = new HashSet<string>(result.Reliability.RestoreFailures, StringComparer.Ordinal);
        foreach (var message in result.LoadDiagnostics)
        {
            var isRestoreFailure = restoreFailures.Contains(message);
            var severity = isRestoreFailure ? LoadSeverity.Error : LoadSeverity.Warning;
            var code = isRestoreFailure ? "loadFailure"
                : message.Contains("unresolved types") ? "unresolvedTypes"
                : "loadWarning";
            result.Reliability.LoadDiagnostics.Add(new LoadDiagnostic(severity, code, message));
        }
    }

    private static bool IsProjectFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
}
