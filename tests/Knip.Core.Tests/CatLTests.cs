using System.Text.Json;
using Json.Schema;
using Knip.Core;
using Knip.Core.Analysis;
using Knip.Core.Configuration;
using Knip.Core.Model;
using Knip.Core.Reporting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category L — the WS8 agent-first interface (JSON v2 output = product API). WS8b-1 ships the FIELD
/// SHAPE; WS8b-2 ships the L9 confidence/hazard DEMOTION engine (hazards advisory-only, first-match
/// demotion C1 → publicApi/C2 → internalsVisibleTo → C3).
///
/// Promoted rows: L1 (output validates against the schema), L2 (stable ids + order across runs),
/// L3 (degraded true vs false), L4 (delete every finding strictly by span → compiles green),
/// L8 (summary == findings), L10 (cascade carries parent id as rootCause; direct == null),
/// and the WS8b-2 demotion rows L11 (global → all low), L12 (per-project attribution), L13/L14
/// (publicApi + config split), L16 (unusedProjectReference → medium; C4 deferred), L17 (IVT → low).
/// Still Skip "G-feat": L5/L6/L7 (--why / --print-config / all-config-key warnings, WS8c) and
/// L15 (serialization/config/DI hazard DETECTION, WS5).
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatLTests
{
    // ── L1: the emitted JSON v2 validates against schemas/knip.output.schema.json ─────────────
    [Fact]
    public async Task L1_output_validates_against_output_schema()
    {
        var json = await RunJson("Main");

        var schema = JsonSchema.FromFile(SchemaPath("knip.output.schema.json"));
        using var doc = JsonDocument.Parse(json);
        var evaluation = schema.Evaluate(
            doc.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(
            evaluation.IsValid,
            "JSON v2 output failed schema validation:\n" +
            string.Join("\n", evaluation.Details
                .Where(d => d.HasErrors)
                .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Value}"))));
    }

    // ── L2: two runs over the same fixture → identical ids AND identical order ────────────────
    [Fact]
    public async Task L2_ids_and_order_are_stable_across_runs()
    {
        var first = ExtractIds(await RunJson("Main"));
        var second = ExtractIds(await RunJson("Main"));

        Assert.NotEmpty(first);
        Assert.Equal(first, second); // ordered comparison: same ids in the same positions.

        // Ids are the stable "k1_" + 10-hex content hash — reproducible, independent of file/line.
        Assert.All(first, id => Assert.Matches("^k1_[0-9a-f]{10}$", id));
    }

    // ── L3: broken/unresolved solution → degraded:true; clean → degraded:false with zeroes ────
    [Fact]
    public async Task L3_degraded_true_on_unresolved_false_on_clean()
    {
        var degraded = await FixtureRunner_Run("Degraded");
        Assert.True(degraded.Reliability.Degraded);
        Assert.True(degraded.Reliability.UnresolvedTypeReferences > 0);

        var clean = await FixtureRunner_Run("Main");
        Assert.False(clean.Reliability.Degraded);
        Assert.Equal(0, clean.Reliability.UnresolvedTypeReferences);
        Assert.Empty(clean.Reliability.ProjectsFailed);
        Assert.Empty(clean.Reliability.RestoreFailures);
    }

    // ── L4: delete every finding strictly by its reported span → the fixture compiles green ───
    [Fact]
    public async Task L4_spans_are_complete_deletion_units()
    {
        var result = await FixtureRunner_Run("Main");

        // Group findings-with-spans by file; delete each span bottom-up so earlier edits don't shift
        // the offsets of later ones. Spans are 1-based inclusive line/column deletion units.
        var byFile = result.Findings
            .Where(f => f.Span is not null)
            .GroupBy(f => f.Span!.File, StringComparer.Ordinal);

        Assert.NotEmpty(byFile);
        var deletedSomething = false;

        foreach (var group in byFile)
        {
            var original = await File.ReadAllTextAsync(group.Key);
            var edited = original;

            // Sort spans by start offset DESCENDING so we delete tail-first.
            var spans = group
                .Select(f => f.Span!)
                .OrderByDescending(s => (s.Start.Line, s.Start.Column))
                .ToList();

            foreach (var span in spans)
            {
                var start = OffsetOf(edited, span.Start.Line, span.Start.Column);
                var end = OffsetOf(edited, span.End.Line, span.End.Column);
                Assert.True(end >= start, $"span end precedes start in {group.Key}");
                edited = edited.Remove(start, end - start);
                deletedSomething = true;
            }

            // The file with every reported deletion unit removed must still parse without errors.
            var tree = CSharpSyntaxTree.ParseText(edited);
            var diagnostics = tree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            Assert.True(
                diagnostics.Count == 0,
                $"deleting reported spans left {group.Key} un-parseable:\n" +
                string.Join("\n", diagnostics.Select(d => "  " + d.GetMessage())) +
                "\n--- edited source ---\n" + edited);
        }

        Assert.True(deletedSomething, "L4 fixture produced no span-bearing findings to delete.");
    }

    // ── L8: summary counts (byKind / byConfidence / byProject) equal the findings array exactly ─
    [Fact]
    public async Task L8_summary_equals_findings()
    {
        var json = await RunJson("Main");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var findings = root.GetProperty("findings").EnumerateArray().ToList();
        var summary = root.GetProperty("summary");

        Assert.Equal(findings.Count, summary.GetProperty("total").GetInt32());

        // byKind and byConfidence must reconstruct exactly from the findings array.
        AssertCountMap(summary.GetProperty("byKind"), findings, f => f.GetProperty("kind").GetString()!);
        AssertCountMap(summary.GetProperty("byConfidence"), findings, f => f.GetProperty("confidence").GetString()!);

        // byProject: one entry per distinct project, per-project totals + maps agree.
        var projectTotals = findings
            .GroupBy(f => f.GetProperty("project").GetString()!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var byProject = summary.GetProperty("byProject").EnumerateArray().ToList();
        Assert.Equal(projectTotals.Count, byProject.Count);
        foreach (var p in byProject)
        {
            var name = p.GetProperty("project").GetString()!;
            Assert.Equal(projectTotals[name], p.GetProperty("total").GetInt32());
            var scoped = findings.Where(f => f.GetProperty("project").GetString() == name).ToList();
            AssertCountMap(p.GetProperty("byKind"), scoped, f => f.GetProperty("kind").GetString()!);
            AssertCountMap(p.GetProperty("byConfidence"), scoped, f => f.GetProperty("confidence").GetString()!);
        }
    }

    // ── L10: cascade finding carries the parent's id as rootCause; direct one carries null ────
    [Fact]
    public async Task L10_rootcause_is_parent_id_for_cascade_null_for_direct()
    {
        var result = await FixtureRunner_Run("Main");

        var caller = Single(result, "CatL.Main.DeadCaller"); // directly unreferenced
        var callee = Single(result, "CatL.Main.DeadCallee"); // kept dead ONLY by DeadCaller's field

        Assert.Null(caller.RootCause);
        Assert.Equal(caller.Id, callee.RootCause);

        // A stand-alone dead type with no dead referrer is also directly unreferenced.
        var documented = Single(result, "CatL.Main.DeadDocumented");
        Assert.Null(documented.RootCause);
    }

    // ══ L9 confidence/hazard demotion engine (WS8b-2). Rows L11–L17 pin the individual rules; each
    //    asserts the TIER (never absence — every finding is still emitted; hazards are advisory). ══

    // ── L11: solution-GLOBAL degradation → ALL findings low (C1 global). Reuses the Degraded fixture,
    //    whose unresolved-type reference drives reliability.degraded end-to-end through KnipEngine. ────
    [Fact]
    public async Task L11_global_degradation_demotes_all_to_low()
    {
        var result = await FixtureRunner_Run("Degraded");

        Assert.True(result.Reliability.Degraded, "L11 precondition: the Degraded fixture must be degraded.");
        Assert.NotEmpty(result.Findings);
        // Global degradation taints the whole graph: nothing may be trusted for autonomous deletion.
        Assert.All(result.Findings, f => Assert.Equal(Confidence.Low, f.Confidence));
    }

    // ── L12: a load failure in ONE project only → that project's findings low, others unaffected
    //    (C1 per-project attribution). Real offline fixtures cannot make MSBuild fail a single project,
    //    so this drives ConfidenceModel directly with a synthetic per-project ProjectsFailed. ──────────
    [Fact]
    public void L12_per_project_failure_demotes_only_that_project()
    {
        var result = new AnalysisResult();
        result.Findings.Add(HighFinding("Bad.Sample", "Bad"));
        result.Findings.Add(HighFinding("Healthy.Sample", "Healthy"));
        // ONE project failed; the solution is NOT globally degraded (no unresolved types / restore fail).
        result.Reliability.ProjectsFailed.Add(new ProjectLoadFailure("Bad", "boom"));

        ConfidenceModel.Apply(result, new KnipConfig());

        Assert.Equal(Confidence.Low, result.Findings.Single(f => f.Project == "Bad").Confidence);
        // A healthy project's finding is NOT demoted by another project's failure.
        Assert.Equal(Confidence.High, result.Findings.Single(f => f.Project == "Healthy").Confidence);
    }

    // ── L13: publicApi-hazard finding, API posture DECLARED (publicApiProjects/treatAllPublicAsUsed
    //    set) → medium (C2 configured branch). The config glob deliberately does NOT match this project,
    //    so the public symbol survives to a finding and is graded medium rather than rooted. ───────────
    [Fact]
    public async Task L13_publicapi_hazard_medium_when_posture_declared()
    {
        // publicApiProjects set (posture declared) but matching a DIFFERENT project name.
        var config = new KnipConfig();
        config.Roots.PublicApiProjects.Add("SomeOther*");

        var result = await FixtureRunner_Run("PublicApi", config);

        var pub = Single(result, "CatL.PublicApi.DeadPublicApi");
        Assert.Contains(Hazard.PublicApi, pub.Hazards);
        Assert.Equal(Confidence.Medium, pub.Confidence);

        // Anti-vacuous: the no-hazard internal sibling stays HIGH — only the publicApi finding is graded.
        Assert.Equal(Confidence.High, Single(result, "CatL.PublicApi.DeadInternalPlain").Confidence);
    }

    // ── L14: publicApi-hazard finding, NEITHER key set → low (C2 unconfigured branch). ────────────────
    [Fact]
    public async Task L14_publicapi_hazard_low_when_posture_unknown()
    {
        var result = await FixtureRunner_Run("PublicApi"); // default config: no publicApi posture declared

        var pub = Single(result, "CatL.PublicApi.DeadPublicApi");
        Assert.Contains(Hazard.PublicApi, pub.Hazards);
        Assert.Equal(Confidence.Low, pub.Confidence);

        // Anti-vacuous: the no-hazard internal sibling stays HIGH.
        Assert.Equal(Confidence.High, Single(result, "CatL.PublicApi.DeadInternalPlain").Confidence);
    }

    // ── L16 (C3 half): an UnusedProjectReference finding → medium. C4 (deleteCodeAndTests) is DEFERRED
    //    to WS7 — no test-only reachability kind exists yet, so only the project-ref half is pinned. ───
    [Fact]
    public async Task L16_unused_project_reference_is_medium()
    {
        var result = await FixtureRunner.RunAsync("WS2");

        Assert.False(result.Reliability.Degraded, "L16 precondition: WS2 fixture must load clean (no C1 global demotion).");
        var projectRef = result.Findings.Single(f =>
            f.Kind == FindingKind.UnusedProjectReference && f.ReferencedProject == "WS2.UnusedLib");
        Assert.Equal(Confidence.Medium, projectRef.Confidence);
    }

    // ── L17: [InternalsVisibleTo] names a NON-solution assembly → this project's INTERNAL findings low
    //    (new internalsVisibleTo hazard). Private findings are unaffected (invisible even to friends). ─
    [Fact]
    public async Task L17_internals_visible_to_non_solution_demotes_internal_to_low()
    {
        var result = await FixtureRunner_Run("InternalsVisibleTo");

        var @internal = Single(result, "CatL.InternalsVisibleTo.DeadInternal");
        Assert.Contains(Hazard.InternalsVisibleTo, @internal.Hazards);
        Assert.Equal(Confidence.Low, @internal.Confidence);

        // Anti-vacuous: a PRIVATE member is invisible even to a friend assembly, so no hazard applies
        // and it stays HIGH — proving IVT tags only internal findings, not everything in the project.
        var priv = result.Findings.Single(f => f.Symbol.EndsWith("DeadPrivate()", StringComparison.Ordinal));
        Assert.Empty(priv.Hazards);
        Assert.Equal(Confidence.High, priv.Confidence);
    }

    // ── L15 left G-feat: serializationShaped/configBoundType/diPluginShaped hazard DETECTION needs
    //    heuristics/plugins (WS5). The enum values + the low-tier demotion off them already exist in the
    //    engine, but with no detector to attach them there is nothing to pin end-to-end yet. ───────────
    [Fact(Skip = "G-feat: serialization/config/DI hazard DETECTION is WS5 (enum + low demotion exist; no detector yet)")]
    public void L15_serialization_config_di_hazards() { }

    // ── Skipped rows (deferred work streams) ──────────────────────────────────────────────────
    [Fact(Skip = "G-feat: --why provenance is WS8c")]
    public void L5_why_flagged_and_alive() { }

    [Fact(Skip = "G-feat: --print-config is WS8c")]
    public void L6_print_config() { }

    [Fact(Skip = "G-feat: generalized unknown-key warnings are WS8c")]
    public void L7_all_config_key_warnings() { }

    // ── helper: a High-confidence, no-hazard symbol finding for the synthetic L12 unit test. ──────────
    private static Finding HighFinding(string symbol, string project) => new(
        FindingKind.UnusedMethod, symbol, "method", "private", project, "X.cs", 1, 1);

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static Finding Single(AnalysisResult result, string symbol) =>
        result.Findings.Single(f => f.Symbol == symbol);

    private static async Task<string> RunJson(string variant)
    {
        var result = await FixtureRunner_Run(variant);
        using var writer = new StringWriter();
        new JsonReporter().Report(result, writer);
        return writer.ToString();
    }

    private static Task<AnalysisResult> FixtureRunner_Run(string variant, KnipConfig? config = null) =>
        KnipEngine.RunAsync(config ?? new KnipConfig(), FixtureSolution(variant));

    private static IReadOnlyList<string> ExtractIds(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("findings").EnumerateArray()
            .Select(f => f.GetProperty("id").GetString()!)
            .ToList();
    }

    private static void AssertCountMap(
        JsonElement map, IEnumerable<JsonElement> findings, Func<JsonElement, string> key)
    {
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in findings)
        {
            var k = key(f);
            expected[k] = expected.TryGetValue(k, out var n) ? n + 1 : 1;
        }

        var actual = map.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal);
        Assert.Equal(expected, actual);
    }

    /// <summary>Char offset of a 1-based line/column in <paramref name="text"/> (LF or CRLF agnostic).</summary>
    private static int OffsetOf(string text, int line, int column)
    {
        var currentLine = 1;
        var i = 0;
        while (currentLine < line && i < text.Length)
        {
            if (text[i] == '\n') currentLine++;
            i++;
        }
        return Math.Min(i + (column - 1), text.Length);
    }

    private static string FixtureSolution(string variant) =>
        Path.Combine(RepoRoot(), "tests", "fixtures", "CatL", variant, "Fixture.slnx");

    private static string SchemaPath(string file) =>
        Path.Combine(RepoRoot(), "schemas", file);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Knip.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException("Could not locate repo root (Knip.slnx) from " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
