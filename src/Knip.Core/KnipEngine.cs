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
        CancellationToken ct = default,
        bool captureProvenance = false)
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
        var result = await analyzer.AnalyzeAsync(solution, progress, ct, captureProvenance);

        foreach (var (_, message) in loadDiagnostics)
        {
            result.LoadDiagnostics.Add(message);

            // WS8: MSBuildWorkspace surfaces benign NuGet restore-audit / pruning advisories (NU1510,
            // the NU19xx vulnerability family) as "Msbuild failed …" workspace FAILURES even though the
            // projects loaded fine. Those must stay VISIBLE (invariant #8) but must NOT be treated as
            // restore failures — otherwise harmless audit noise flips `degraded` and nukes confidence on
            // a good solution. Everything else is still a genuine load failure (invariant #6: stay LOUD)
            // and is attributed as a restore failure → degraded => true.
            if (!WorkspaceDiagnosticClassifier.IsBenignNuGetAdvisory(message))
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
                // WS8: a benign NuGet restore-audit / pruning advisory that was NOT recorded as a restore
                // failure — surfaced as a warning so a human/agent still sees it, but it does not degrade.
                : Analysis.WorkspaceDiagnosticClassifier.IsBenignNuGetAdvisory(message) ? "nugetAdvisory"
                : "loadWarning";
            result.Reliability.LoadDiagnostics.Add(new LoadDiagnostic(severity, code, message));
        }
    }

    private static bool IsProjectFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
}
