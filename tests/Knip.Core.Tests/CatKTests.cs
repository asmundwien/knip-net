using Knip.Core.Configuration;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category K — test-only reachability. In DEFAULT mode every [Fact]/[Theory] method is a root, so
/// production code referenced ONLY by its own tests is reachable and never flagged (a deliberate
/// false negative). A future WS7 "production mode" (--production / testProjects config) will flag such
/// code as a distinct OnlyUsedByTests finding.
///
/// Row status:
///   K1  Contract  (DEFAULT mode: test-only production code is ALIVE)              — GREEN
///   K4  Contract  (WORKAROUND: ignore.projects excluding the test project flags it) — GREEN
///   K2  G-feat    (production mode: OnlyUsedByTests finding)                        — SKIPPED (WS7)
///   K3  G-feat    (production mode: finding lists referencing test symbols)         — SKIPPED (WS7)
///   K5  G-feat    (production mode: transitive test-only chain)                     — SKIPPED (WS7)
///   K6  G-feat    (production mode: prod+test usage never flagged)                  — SKIPPED (WS7)
///   K7  Decision  (test-project classification default; zero-test-project warning)  — SKIPPED
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatKTests
{
    private const string Category = "CatK";

    private static Task<IReadOnlySet<string>> FindingsIn(string ns, KnipConfig? config = null) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns, config);

    // ---- K1: Contract — DEFAULT mode keeps test-only production code alive -------------------

    [Trait("status", "contract")]
    [Fact] // K1: production method reached only from a [Fact] is ALIVE; never-called sibling flagged.
    public async Task K1_test_only_production_method_is_alive_in_default_mode()
    {
        var findings = await FindingsIn("CatK.K1");

        // ALIVE (asserted by ABSENCE): Sample.UsedOnlyByTest — its sole caller is a [Fact] root, and
        // in default mode test roots keep production code reachable. This documents the deliberate
        // false negative that WS7 production mode will later surface as OnlyUsedByTests.
        // DEAD SIBLING (the anti-vacuous anchor): Sample.NeverCalled has no caller at all -> flagged.
        // Its presence proves the analyzer DID examine this type, so UsedOnlyByTest's absence from the
        // finding set is a real "alive" verdict, not a vacuous empty result.
        Assert.Equal(
            new HashSet<string> { "CatK.K1.Sample.NeverCalled()" },
            findings);
    }

    // ---- K4: Contract — WORKAROUND via ignore.projects (VERIFY) ------------------------------

    [Trait("status", "contract")]
    [Fact] // K4: ignoring the *Tests* project removes the sole caller -> test-only method flagged.
    public async Task K4_ignoring_test_project_flags_the_test_only_production_method()
    {
        // The mutation of K1's setup expressed as a config flip on a genuine 2-project fixture:
        // config.Ignore.Projects skips the CatK.K4.Tests project entirely, so its [Fact] root vanishes
        // and CatK.K4.Widget.TestOnly loses its only caller.
        var config = new KnipConfig();
        config.Ignore.Projects = ["*Tests*"];

        var findings = await FindingsIn("CatK.K4", config);

        // FLAGGED: TestOnly (its only caller lived in the now-ignored test project) AND the DEAD
        // SIBLING NeverCalled (no caller in any config). NeverCalled is the anti-vacuous anchor: it is
        // flagged in BOTH configs, so TestOnly's appearance here is caused by the ignore flip, not by
        // the Lib being unanalyzed.
        Assert.Equal(
            new HashSet<string>
            {
                "CatK.K4.Widget.TestOnly()",
                "CatK.K4.Widget.NeverCalled()",
            },
            findings);
    }

    // ---- K2..K6: G-feat — future WS7 production mode (SKIPPED; feature not built) -------------

    [Trait("status", "feat")]
    [Fact(Skip = "K2 — WS7: production mode")]
    public async Task K2_production_mode_flags_test_only_code_as_OnlyUsedByTests()
    {
        // WS7 intent: under --production / testProjects config, the test roots are demoted, so a
        // production method + type reachable only via test roots is reported. The distinct finding
        // kind is OnlyUsedByTests (separate from ordinary Unused* so it can be triaged differently).
        // Expressed against today's API surface as best available: the symbol should be flagged.
        var findings = await FindingsIn("CatK.K2" /* , config with production mode */);
        Assert.Contains("CatK.K2.Service.ProductionMethod()", findings);
        Assert.Contains("CatK.K2.Service", findings);
    }

    [Trait("status", "feat")]
    [Fact(Skip = "K3 — WS7: production mode")]
    public async Task K3_production_mode_finding_lists_referencing_test_symbols()
    {
        // WS7 intent: the OnlyUsedByTests finding for Calculator.Add must enumerate its referring test
        // symbols (CatK.K3.AlphaTests.Adds, CatK.K3.BetaTests.AlsoAdds) so a human sees the remediation
        // unit. The current Finding record has no "referrers" field; this row pins the requirement that
        // WS7 add one and populate it. Placeholder assertion until the field exists.
        var findings = await FindingsIn("CatK.K3");
        Assert.Contains("CatK.K3.Calculator.Add(int, int)", findings);
    }

    [Trait("status", "feat")]
    [Fact(Skip = "K5 — WS7: production mode")]
    public async Task K5_production_mode_flags_transitive_test_only_chain()
    {
        // WS7 intent: A is used only by B, B is used only by tests -> under production mode BOTH are
        // test-only-reachable and flagged OnlyUsedByTests.
        var findings = await FindingsIn("CatK.K5");
        Assert.Contains("CatK.K5.Chain.A()", findings);
        Assert.Contains("CatK.K5.Chain.B()", findings);
    }

    [Trait("status", "feat")]
    [Fact(Skip = "K6 — WS7: production mode")]
    public async Task K6_production_mode_does_not_flag_code_used_by_production_and_tests()
    {
        // WS7 intent: tests must not TAINT liveness. Shared.UsedByBoth has a genuine production caller
        // (ProductionEntry.Main) as well as a test caller, so even in production mode it is NOT
        // OnlyUsedByTests and the K6 finding set stays empty.
        var findings = await FindingsIn("CatK.K6" /* , config with production mode */);
        Assert.DoesNotContain("CatK.K6.Shared.UsedByBoth()", findings);
    }

    // ---- K7: Decision — test-project classification default (SKIPPED; reported to human) ------

    [Trait("status", "decision")]
    [Fact(Skip = "K7 — decision pending: test-project classification default")]
    public async Task K7_test_project_classification_default_is_undecided()
    {
        // DECISION for the human (see final report). Two coupled questions for WS7 production mode:
        //   1. How is a project classified as a "test project"? Candidates:
        //        (a) MSBuild <IsTestProject>true</IsTestProject> — explicit but often unset;
        //        (b) presence of a test-framework package reference (xunit/nunit/mstest) — invisible to
        //            the offline, zero-NuGet fixtures used here;
        //        (c) a project-name glob ("*Tests", "*.Test(s)") — cheap, aligns with the K4 workaround.
        //   2. Should production mode WARN when ZERO test projects are detected? That likely signals
        //      misconfiguration (every test-only symbol would flip to a finding and drown the report).
        // No answer is pinned; this row only records the open question.
        var findings = await FindingsIn("CatK.K7");
        Assert.NotNull(findings);
    }
}
