using System.Text.RegularExpressions;

namespace Knip.Core.Analysis;

/// <summary>
/// Classifies an MSBuildWorkspace failure diagnostic into a genuine project-LOAD failure vs. benign
/// NuGet-advisory NOISE (WS8).
/// <para>
/// MSBuildWorkspace surfaces restore-audit / pruning NuGet warnings — <c>NU1510</c> ("PackageReference …
/// will not be pruned"), the <c>NU19xx</c> vulnerability/audit family (e.g. <c>NU1903</c> "known high
/// severity vulnerability") — as workspace <c>Failure</c> diagnostics worded
/// <c>"Msbuild failed when processing the file '…' with message: &lt;the NuGet warning&gt;"</c>.
/// These are NOT load failures: the projects still compile and load fine (<c>projectsLoaded</c> is full,
/// <c>projectsFailed</c> empty, no unresolved types). Treating them as restore failures wrongly flips
/// <see cref="Model.Reliability.Degraded"/> and nukes confidence on a perfectly good solution.
/// </para>
/// <para>
/// This classifier ONLY reclassifies the identifiable NuGet-advisory noise into a benign WARNING. It is
/// deliberately CONSERVATIVE (invariant #6 — stay LOUD on real problems): any "Msbuild failed" message
/// that is NOT recognisable as a NuGet advisory is still treated as a genuine load failure and continues
/// to degrade the run. The advisory warnings stay VISIBLE in the load-diagnostics channel (invariant #8 —
/// recall over silence); they simply do not set <c>degraded</c>.
/// </para>
/// </summary>
public static class WorkspaceDiagnosticClassifier
{
    // A NuGet warning/error code: NU followed by exactly four digits (e.g. NU1510, NU1903, NU1701).
    private static readonly Regex NuGetCode = new(@"\bNU\d{4}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Tell-tale phrasing of the pruning / audit / vulnerability advisories, in case the numeric code is
    // absent from the surfaced message. Matched case-insensitively.
    private static readonly string[] AdvisoryPhrases =
    [
        "will not be pruned",
        "vulnerability",
        "consider removing this package",
    ];

    /// <summary>
    /// True when <paramref name="message"/> is a benign NuGet restore-audit / pruning advisory that
    /// MSBuildWorkspace surfaced as a workspace failure — i.e. it carries a <c>NU19xx</c>/<c>NU1510</c>
    /// (any <c>NU\d{4}</c>) code OR a known advisory phrase. Such a diagnostic must be recorded as a
    /// WARNING, NOT a restore failure, and must NOT contribute to <c>degraded</c>.
    /// </summary>
    public static bool IsBenignNuGetAdvisory(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        if (NuGetCode.IsMatch(message))
            return true;

        foreach (var phrase in AdvisoryPhrases)
            if (message.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

        return false;
    }
}
