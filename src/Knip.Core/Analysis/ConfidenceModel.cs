using Knip.Core.Configuration;
using Knip.Core.Model;

namespace Knip.Core.Analysis;

/// <summary>
/// The L9 confidence &amp; hazard demotion engine (WS8 §4, SIGNED OFF 2026-07-15). Implements the REVISED
/// invariant #8 ("recall over silence — but hazards are sacred"): findings are NEVER suppressed, only
/// graded. Hazards are ADVISORY — they never change the emitted set, and a finding's presence/absence is
/// unaffected. Confidence starts <see cref="Confidence.High"/> and is demoted by the FIRST matching rule:
/// <list type="number">
///   <item>C1 (per-project reliability): a project that failed to load/restore, OR solution-GLOBAL
///     degradation, demotes to <see cref="Confidence.Low"/>. Per-project attribution — a healthy
///     project's findings are NOT demoted by another project's failure.</item>
///   <item>C2 (publicApi, config-sensitive): a <see cref="Hazard.PublicApi"/> finding →
///     <see cref="Confidence.Medium"/> when the user declared their API posture
///     (<c>publicApiProjects</c> OR <c>treatAllPublicAsUsed</c>), else <see cref="Confidence.Low"/>.
///     Other C2 hazards (serialization/config/DI) → <see cref="Confidence.Low"/>.</item>
///   <item>InternalsVisibleTo: a <see cref="Hazard.InternalsVisibleTo"/> finding →
///     <see cref="Confidence.Low"/> (invisible external consumer).</item>
///   <item>WS3 build-only package (<see cref="Hazard.BuildOnlyPackage"/>: analyzer / source-generator /
///     <c>PrivateAssets="all"</c> — no referenceable compile assembly) → <see cref="Confidence.Low"/>,
///     below the C3 medium a normal package-ref gets.</item>
///   <item>C3: project/package-reference findings → <see cref="Confidence.Medium"/>.</item>
/// </list>
/// A finding with no hazard in a healthy project stays <see cref="Confidence.High"/>.
/// C4 (deleteCodeAndTests, WS7 OnlyUsedByTests) → <see cref="Confidence.Medium"/>, applied right after C1
/// so the remediation shape dominates the publicApi hazard (test-only code is nearly always public).
/// C5 (entry-point near-miss, DROPPED from v1) and serialization/config/DI hazard DETECTION (WS5) are
/// DEFERRED — not applied here.
/// </summary>
internal static class ConfidenceModel
{
    /// <summary>
    /// Grade every finding's confidence in place, using the FINAL reliability picture (so C1 sees
    /// project-load/restore failures attributed by the engine) plus the hazards already attached by the
    /// analyzer. Idempotent given the same inputs.
    /// </summary>
    public static void Apply(AnalysisResult result, KnipConfig config)
    {
        var apiPostureDeclared =
            config.Roots.TreatAllPublicAsUsed || config.Roots.PublicApiProjects.Count > 0;

        // Solution-GLOBAL degradation demotes EVERYTHING. Per-project failures demote only their own
        // project's findings (C1 attribution). Global signals are the ones not tied to a single project:
        // unresolved-type references (a solution-wide restore signal) and workspace/restore failures that
        // carry no project attribution.
        var globalDegradation = IsGloballyDegraded(result.Reliability);
        var failedProjects = result.Reliability.ProjectsFailed
            .Select(p => p.Project)
            .ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < result.Findings.Count; i++)
        {
            var finding = result.Findings[i];
            var confidence = Compute(finding, apiPostureDeclared, globalDegradation, failedProjects);
            if (confidence != finding.Confidence)
                result.Findings[i] = finding with { Confidence = confidence };
        }
    }

    /// <summary>First-match demotion: C1 → C4 → (PublicApi/C2) → InternalsVisibleTo → C3; else High.</summary>
    private static Confidence Compute(
        Finding finding, bool apiPostureDeclared, bool globalDegradation, HashSet<string> failedProjects)
    {
        // C1 — per-project reliability. Global degradation demotes all; otherwise only the affected project.
        // (Takes precedence over C4: a degraded graph can't be trusted to classify test projects either.)
        if (globalDegradation || failedProjects.Contains(finding.Project))
            return Confidence.Low;

        // C4 — deleteCodeAndTests (WS7 OnlyUsedByTests): the WS7 card pins this kind at MEDIUM regardless
        // of accessibility (test-only production code is nearly always public — it exists to be called by
        // a test). So the DeleteCodeAndTests remediation SHAPE dominates the publicApi hazard here: the
        // finding is medium (propose in the PR; a human confirms the referrer set + classification). Below
        // C1 so a degraded run still demotes it to low.
        if (finding.Remediation is Remediation.DeleteCodeAndTests)
            return Confidence.Medium;

        // C2 — publicApi (config-sensitive): declared posture → medium, unknown exposure → low.
        if (finding.Hazards.Contains(Hazard.PublicApi))
            return apiPostureDeclared ? Confidence.Medium : Confidence.Low;

        // Other C2 hazards (serialization/config/DI) → low. Detection is deferred (WS5), but honour any
        // that a plugin lane may attach later.
        if (finding.Hazards.Contains(Hazard.SerializationShaped)
            || finding.Hazards.Contains(Hazard.ConfigBoundType)
            || finding.Hazards.Contains(Hazard.DiPluginShaped))
            return Confidence.Low;

        // WS3 build-only / analyzer / source-generator package (no referenceable compile assembly): its
        // effect is invisible to symbol edges, so an "unused" verdict is unreliable → low (below the C3
        // medium a normal package-ref gets). Emitted, never dropped (REVISED §3.8).
        if (finding.Hazards.Contains(Hazard.BuildOnlyPackage))
            return Confidence.Low;

        // InternalsVisibleTo — an invisible external consumer may bind this internal symbol → low.
        if (finding.Hazards.Contains(Hazard.InternalsVisibleTo))
            return Confidence.Low;

        // C3 — project/package-reference findings are conservative by construction → medium.
        if (finding.Remediation is Remediation.RemoveProjectReference or Remediation.RemovePackageReference)
            return Confidence.Medium;

        // C5 (entry-point near-miss) DROPPED from v1 — not applied here.
        return Confidence.High;
    }

    /// <summary>
    /// Solution-GLOBAL degradation: signals that taint the whole graph rather than one project.
    /// Unresolved-type references and un-attributed restore/load failures are treated as global (they are
    /// not tied to a single project). Per-project failures live in <see cref="Reliability.ProjectsFailed"/>
    /// and are handled by C1 attribution, NOT here.
    /// </summary>
    private static bool IsGloballyDegraded(Reliability reliability) =>
        reliability.UnresolvedTypeReferences > 0
        || reliability.RestoreFailures.Count > 0
        || reliability.LoadDiagnostics.Any(d => d.Severity == LoadSeverity.Error);
}
