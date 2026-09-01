using Knip.Core.Configuration;
using Knip.Core.Model;

namespace Knip.Core.Analysis;

/// <summary>
/// Grades findings without suppressing them. Hazards are advisory: they change confidence, never the emitted
/// set. Local confidence starts <see cref="Confidence.High"/> and is demoted by the first matching rule:
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
/// C4 (deleteCodeAndTests, WS7 OnlyUsedByTests) → <see cref="Confidence.Medium"/>, applied LAST — after
/// C2/IVT/C3 (HUMAN DECISION 2026-07-15, §6). C2 (publicApi) now PRECEDES C4, so an unconfigured-public
/// test-only finding lands <see cref="Confidence.Low"/> (the verify loop is structurally blind to an
/// unknown external consumer — deleting the symbol + its tests goes green by construction); a
/// configured-but-not-listed public test-only finding lands medium; an internal/private test-only finding
/// (no publicApi hazard) falls through to C4 → medium.
/// </summary>
internal static class ConfidenceModel
{
    /// Grade every finding's local confidence using the FINAL reliability picture and attached hazards,
    /// then cap each descendant by every finding in its <see cref="Finding.RootCause"/> chain. The published
    /// confidence is therefore the effective autonomy tier of the complete outer deletion unit. Idempotent
    /// given the same inputs.
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

        // Confidence governs action on the complete deletion unit, not an isolated nested declaration.
        // Hazards remain local facts; inheriting their resulting tier avoids claiming a private child is
        // autonomously safe when its public or runtime-shaped ancestor is not.
        ApplyRootCauseCeilings(result.Findings);

        static void ApplyRootCauseCeilings(IList<Finding> findings)
        {
            var hasRootCause = false;
            for (var i = 0; i < findings.Count; i++)
                if (findings[i].RootCause is not null)
                {
                    hasRootCause = true;
                    break;
                }
            if (!hasRootCause) return;

            var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < findings.Count; i++)
                if (findings[i].Id.Length > 0)
                    indexById[findings[i].Id] = i;
            var resolved = new bool[findings.Count];
            var resolving = new bool[findings.Count];

            for (var i = 0; i < findings.Count; i++)
                Resolve(i);

            Confidence Resolve(int index)
            {
                if (resolved[index]) return findings[index].Confidence;
                if (resolving[index]) return Confidence.Low;

                resolving[index] = true;
                var finding = findings[index];
                var confidence = finding.Confidence;
                if (finding.RootCause is { } rootCause && indexById.TryGetValue(rootCause, out var parentIndex))
                    confidence = MostRestrictive(confidence, Resolve(parentIndex));

                if (confidence != finding.Confidence)
                    findings[index] = finding with { Confidence = confidence };
                resolving[index] = false;
                resolved[index] = true;
                return confidence;
            }
        }

        static Confidence MostRestrictive(Confidence own, Confidence ancestor)
        {
            if (own is Confidence.Low || ancestor is Confidence.Low) return Confidence.Low;
            if (own is Confidence.Medium || ancestor is Confidence.Medium) return Confidence.Medium;
            return Confidence.High;
        }
    }

    /// <summary>First-match demotion: C1 → C2 (publicApi) → InternalsVisibleTo → C3 → C4; else High.</summary>
    private static Confidence Compute(
        Finding finding, bool apiPostureDeclared, bool globalDegradation, HashSet<string> failedProjects)
    {
        // C1 — per-project reliability. Global degradation demotes all; otherwise only the affected project.
        // (Takes precedence over C4: a degraded graph can't be trusted to classify test projects either.)
        if (globalDegradation || failedProjects.Contains(finding.Project))
            return Confidence.Low;

        // C2 — publicApi (config-sensitive): declared posture → medium, unknown exposure → low.
        // HUMAN DECISION 2026-07-15 (§6): C2 precedes C4. An unconfigured-public test-only finding lands
        // LOW, not medium. The verify loop (delete → build → tests → re-run) is what licenses medium/high
        // autonomy, and it is STRUCTURALLY BLIND here: deleting a public test-only symbol together with its
        // tests makes the loop go green by construction (the only witnesses to its use are deleted with it);
        // an external consumer — unknown, since no publicApi config — breaks at THEIR build, outside every
        // gate we control (the same "survives our gates, breaks elsewhere" shape as category H, §3.8-sacred).
        // A CONFIGURED-but-not-listed public test-only finding still lands medium (posture declared).
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

        // C4 — deleteCodeAndTests (WS7 OnlyUsedByTests), LAST (HUMAN DECISION 2026-07-15, §6). A test-only
        // finding that reached here is NOT externally visible (public would have been graded by C2 above)
        // and carries no other hazard: an internal/private test-only symbol whose only consumers live in
        // the solution's own test projects. The verify loop CAN witness its use here, so the remediation
        // shape licenses medium (propose in the PR; a human confirms the referrer set + classification).
        if (finding.Remediation is Remediation.DeleteCodeAndTests)
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
