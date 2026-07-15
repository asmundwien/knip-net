using Knip.Core;
using Knip.Core.Configuration;
using Knip.Core.Model;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category K — test-only reachability. In DEFAULT mode every [Fact]/[Theory] method is a root, so
/// production code referenced ONLY by its own tests is reachable and never flagged (a deliberate false
/// negative). WS7 "production mode" (--production / config.Production) runs TWO-COLOR reachability: a
/// symbol reachable ONLY via test roots is reported as a distinct OnlyUsedByTests finding (remediation
/// DeleteCodeAndTests), carrying its referencing test symbols so the deletion unit is visible.
///
/// Row status (post-WS7):
///   K1  Contract  (DEFAULT mode: test-only production code is ALIVE)                — GREEN
///   K4  Contract  (WORKAROUND: ignore.projects excluding the test project flags it) — GREEN
///   K2  Contract  (production mode: OnlyUsedByTests finding)                         — GREEN
///   K3  Contract  (production mode: finding lists referencing test symbols)          — GREEN
///   K5  Contract  (production mode: transitive test-only chain -> both flagged)      — GREEN
///   K6  Contract  (production mode: prod+test usage never flagged)                   — GREEN
///   K7  Contract  (classification per signal + zero-test-project warning)            — GREEN
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatKTests
{
    private const string Category = "CatK";

    private static Task<IReadOnlySet<string>> FindingsIn(string ns, KnipConfig? config = null) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns, config);

    /// <summary>Full findings scoped to a scenario namespace (kind/remediation/referrers assertions).</summary>
    private static async Task<IReadOnlyList<Finding>> FindingObjectsIn(string ns, KnipConfig? config = null)
    {
        var result = await FixtureRunner.RunAsync(Category, config);
        var prefix = ns + ".";
        return result.Findings.Where(f => f.Symbol.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    private static KnipConfig Production()
    {
        var config = new KnipConfig { Production = true };
        return config;
    }

    // ---- K1: Contract — DEFAULT mode keeps test-only production code alive -------------------

    [Trait("status", "contract")]
    [Fact] // K1: production method reached only from a [Fact] is ALIVE; never-called sibling flagged.
    public async Task K1_test_only_production_method_is_alive_in_default_mode()
    {
        var findings = await FindingsIn("CatK.K1");

        // ALIVE (asserted by ABSENCE): Sample.UsedOnlyByTest — its sole caller is a [Fact] root, and
        // in default mode test roots keep production code reachable. This documents the deliberate
        // false negative that WS7 production mode surfaces as OnlyUsedByTests.
        // DEAD SIBLING (the anti-vacuous anchor): Sample.NeverCalled has no caller at all -> flagged.
        Assert.Equal(
            new HashSet<string> { "CatK.K1.Sample.NeverCalled()" },
            findings);
    }

    [Trait("status", "contract")]
    [Fact] // K1 (production mode still respects K1's own default-mode fixture: no [Fact] project here).
    public async Task K1_default_mode_finding_set_unchanged_by_production_flag_absence()
    {
        // Guard: K1 (the contract above) is a DEFAULT-mode row; production mode is a DIFFERENT verdict on
        // the SAME fixture. Under production mode K1.Sample has NO production caller, so the whole TYPE is
        // reachable only via the test -> the outermost OnlyUsedByTests finding is the TYPE (§3.7), which
        // subsumes both members. This captures the red-flip as a positive check without mutating K1.
        var findings = await FindingObjectsIn("CatK.K1", Production());
        var type = findings.Single(f => f.Symbol == "CatK.K1.Sample");
        Assert.Equal(FindingKind.OnlyUsedByTests, type.Kind);
        Assert.DoesNotContain(findings, f => f.Symbol == "CatK.K1.Sample.UsedOnlyByTest()");
        Assert.DoesNotContain(findings, f => f.Symbol == "CatK.K1.Sample.NeverCalled()");
    }

    // ---- K4: Contract — WORKAROUND via ignore.projects (VERIFY) ------------------------------

    [Trait("status", "contract")]
    [Fact] // K4: ignoring the *Tests* project removes the sole caller -> test-only method flagged.
    public async Task K4_ignoring_test_project_flags_the_test_only_production_method()
    {
        var config = new KnipConfig();
        config.Ignore.Projects = ["*Tests*"];

        var findings = await FindingsIn("CatK.K4", config);

        Assert.Equal(
            new HashSet<string>
            {
                "CatK.K4.Widget.TestOnly()",
                "CatK.K4.Widget.NeverCalled()",
            },
            findings);
    }

    // ---- K2: Contract — production mode flags test-only code as OnlyUsedByTests --------------

    [Trait("status", "contract")]
    [Fact]
    public async Task K2_production_mode_flags_test_only_code_as_OnlyUsedByTests()
    {
        // DEFAULT mode: ProductionMethod is ALIVE (test root keeps it), only NeverCalled is flagged.
        var defaultFindings = await FindingsIn("CatK.K2");
        Assert.DoesNotContain("CatK.K2.Service.ProductionMethod()", defaultFindings);
        Assert.Contains("CatK.K2.Service.NeverCalled()", defaultFindings);

        // PRODUCTION mode: the test root is demoted, so ProductionMethod is reachable ONLY via tests ->
        // OnlyUsedByTests. NeverCalled remains plain-dead (UnusedMethod). The Service TYPE stays alive
        // (Entry.Main -> KeepAlive), so the OnlyUsedByTests finding is at MEMBER granularity.
        var findings = await FindingObjectsIn("CatK.K2", Production());

        var testOnly = findings.Single(f => f.Symbol == "CatK.K2.Service.ProductionMethod()");
        Assert.Equal(FindingKind.OnlyUsedByTests, testOnly.Kind);
        Assert.Equal(Remediation.DeleteCodeAndTests, testOnly.Remediation);
        // ProductionMethod is PUBLIC → carries the publicApi hazard. HUMAN DECISION 2026-07-15 (§6): C2
        // (publicApi) now PRECEDES C4, and no publicApi posture is declared here, so this unconfigured-
        // public test-only finding lands LOW (was medium under the old C4-before-C2 order). The verify
        // loop is structurally blind to an unknown external consumer of a public test-only symbol.
        Assert.Equal(Confidence.Low, testOnly.Confidence); // C2 (publicApi, unconfigured) — precedes C4

        // The dead sibling stays an ordinary UnusedMethod — OnlyUsedByTests is a DISTINCT kind.
        var plainDead = findings.Single(f => f.Symbol == "CatK.K2.Service.NeverCalled()");
        Assert.Equal(FindingKind.UnusedMethod, plainDead.Kind);

        // KeepAlive (real production caller) is never flagged in either mode (anti-vacuous / no over-flag).
        Assert.DoesNotContain(findings, f => f.Symbol == "CatK.K2.Service.KeepAlive()");
    }

    // ---- K3: Contract — the OnlyUsedByTests finding lists referencing test symbols -----------

    [Trait("status", "contract")]
    [Fact]
    public async Task K3_production_mode_finding_lists_referencing_test_symbols()
    {
        var findings = await FindingObjectsIn("CatK.K3", Production());

        var finding = findings.Single(f => f.Symbol == "CatK.K3.Calculator.Add(int, int)");
        Assert.Equal(FindingKind.OnlyUsedByTests, finding.Kind);

        // K3: BOTH referring [Fact] tests are enumerated (the "delete the tests too" half), by display
        // name, deterministically ordered, with file:line and never a graph key.
        var referrers = finding.TestReferrers.Select(r => r.Symbol).ToList();
        Assert.Equal(
            new[] { "CatK.K3.AlphaTests.Adds()", "CatK.K3.BetaTests.AlsoAdds()" },
            referrers);
        Assert.All(finding.TestReferrers, r =>
        {
            Assert.True(r.Line > 0);
            Assert.EndsWith("K3.cs", r.File);
        });
    }

    // ---- K5: Contract — transitive test-only chain flags BOTH links --------------------------

    [Trait("status", "contract")]
    [Fact]
    public async Task K5_production_mode_flags_transitive_test_only_chain()
    {
        var findings = await FindingObjectsIn("CatK.K5", Production());

        var a = findings.Single(f => f.Symbol == "CatK.K5.Chain.A()");
        var b = findings.Single(f => f.Symbol == "CatK.K5.Chain.B()");
        Assert.Equal(FindingKind.OnlyUsedByTests, a.Kind); // transitively test-only (A <- B <- test)
        Assert.Equal(FindingKind.OnlyUsedByTests, b.Kind); // directly test-only (B <- test)

        // KeepAlive (production caller) keeps the TYPE alive and is never flagged.
        Assert.DoesNotContain(findings, f => f.Symbol == "CatK.K5.Chain.KeepAlive()");
    }

    // ---- K6: Contract — code used by production AND tests is never OnlyUsedByTests -----------

    [Trait("status", "contract")]
    [Fact]
    public async Task K6_production_mode_does_not_flag_code_used_by_production_and_tests()
    {
        // Even in production mode, Shared.UsedByBoth has a genuine production caller (ProductionEntry.
        // Main) so tests do not TAINT liveness -> not OnlyUsedByTests, and the K6 set is empty.
        var findings = await FindingObjectsIn("CatK.K6", Production());
        Assert.DoesNotContain(findings, f => f.Symbol == "CatK.K6.Shared.UsedByBoth()");
        Assert.Empty(findings);
    }

    // ---- K7: Contract — classification per signal + zero-test-project warning ----------------

    [Trait("status", "contract")]
    [Fact]
    public async Task K7_name_glob_classifies_test_project_end_to_end()
    {
        // Signal 3 (name glob) end-to-end on the CatK fixture: K4.Tests matches "*.Tests" -> test; the
        // production-named projects -> production, signal "default". Surfaced in reliability for -v/JSON.
        var result = await FixtureRunner.RunAsync(Category, Production());
        var byProject = result.Reliability.TestProjectClassifications
            .ToDictionary(c => c.Project, StringComparer.Ordinal);

        Assert.Equal("test", byProject["CatK.K4.Tests"].Kind);
        Assert.Equal("nameGlob:*Tests", byProject["CatK.K4.Tests"].Signal);

        Assert.Equal("production", byProject["CatK"].Kind);
        Assert.Equal("default", byProject["CatK"].Signal);

        // A test project WAS detected -> no zero-detection warning.
        Assert.Empty(result.Reliability.ProductionModeWarnings);
    }

    [Trait("status", "contract")]
    [Fact]
    public async Task K7_explicit_testProjects_glob_overrides_and_reclassifies()
    {
        // Signal 1 (explicit testProjects glob) OVERRIDES name/assembly signals. Classify CatK.K4.Lib
        // (a production-named project) as a test project via config; it then classifies test with the
        // testProjects signal.
        var config = new KnipConfig { Production = true, TestProjects = ["CatK.K4.Lib"] };
        var result = await FixtureRunner.RunAsync(Category, config);
        var lib = result.Reliability.TestProjectClassifications.Single(c => c.Project == "CatK.K4.Lib");

        Assert.Equal("test", lib.Kind);
        Assert.Equal("testProjects:CatK.K4.Lib", lib.Signal);
    }

    [Trait("status", "contract")]
    [Fact]
    public async Task K7_zero_test_projects_warns_loudly_and_never_fails()
    {
        // A solution with NO test project (single production-named "App", no test-framework assembly,
        // no testProjects config). Production mode must WARN LOUDLY (machine diagnostics) and NEVER
        // fail; plain-dead findings still emit (analysis ran).
        var result = await KnipEngine.RunAsync(
            Production(), FixtureRunner.ResolveFixtureSolution("CatK7NoTests"));

        Assert.All(result.Reliability.TestProjectClassifications, c => Assert.Equal("production", c.Kind));
        Assert.NotEmpty(result.Reliability.ProductionModeWarnings);
        Assert.Contains(result.Reliability.ProductionModeWarnings, w => w.Contains("zero test projects", StringComparison.OrdinalIgnoreCase));

        // Never fails: production-mode warnings do NOT set degraded (they change finding MEANING).
        Assert.False(result.Reliability.Degraded);

        // Analysis ran: the whole-dead Service type is flagged (ordinary UnusedType, outermost-only),
        // never OnlyUsedByTests. No OnlyUsedByTests finding exists at all (zero test projects).
        Assert.Contains(result.Findings, f => f.Symbol == "CatK7NoTests.Service" && f.Kind == FindingKind.UnusedType);
        Assert.DoesNotContain(result.Findings, f => f.Kind == FindingKind.OnlyUsedByTests);
    }
}
