using System.Diagnostics;
using Knip.Core;
using Knip.Core.Configuration;
using Knip.Core.Model;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category I — configuration, ignores, discovery and load diagnostics. All rows are Contract.
///
/// This category owns SEVERAL fixture solutions (a single Fixture.slnx per category does not fit),
/// so the in-proc rows resolve solutions by explicit path and call <see cref="KnipEngine.RunAsync"/>
/// directly rather than through FixtureRunner's one-solution-per-category convention. I6 invokes the
/// BUILT CLI out-of-process (a private RepoRoot walk-up + `dotnet run --no-build`, copied — not
/// shared — from CatJ), because a malformed knip.json is a process-boundary contract.
///
/// ANTI-VACUOUS-GREEN: every "not reported because ignored/skipped" assertion is paired with a
/// non-ignored dead SIBLING asserted flagged, or with RED-FLIP evidence (with/without the config).
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatITests
{
    // ── I1: ignore.files glob — matching file neither reported nor walked ─────────────────────
    [Fact] // Sibling: KeptDead in a non-ignored file IS flagged (proves the fixture reports).
    public async Task I1_ignore_files_glob_suppresses_and_skips_walk()
    {
        var config = new KnipConfig { Ignore = { Files = ["**/I1.Ignored.cs"] } };
        var findings = await MainFindingsIn("CatI.I1", config);

        // KeptDead flagged (dead sibling in non-ignored file); IgnoredDead absent (file skipped).
        Assert.Equal(new HashSet<string> { "CatI.I1.KeptDead.KeptDeadMethod()" }, findings);

        // RED-FLIP: with an EMPTY ignore.files, the ignored file's dead symbol IS reported.
        var noIgnore = new KnipConfig { Ignore = { Files = [] } };
        var both = await MainFindingsIn("CatI.I1", noIgnore);
        Assert.Contains("CatI.I1.IgnoredDead.NeverWalked()", both);
        Assert.Contains("CatI.I1.KeptDead.KeptDeadMethod()", both);
    }

    // ── I2: ignore.symbols FQN glob — matching symbol not reported (still in the graph) ───────
    [Fact] // Sibling: ReportedDead (no glob match) IS flagged.
    public async Task I2_ignore_symbols_fqn_glob_suppresses_report()
    {
        // SURFACED BEHAVIOUR: ignore.symbols matches a METHOD by its BARE display name
        // (FullyQualifiedFormat renders a method as just "IgnoredDeadMethod" — no namespace/type/
        // parens), and a TYPE by its FQN ("CatI.I2.Sample"). See the report's "surprises". The glob
        // therefore targets the bare member name; the contract (matching symbol suppressed) holds.
        var config = new KnipConfig { Ignore = { Symbols = ["IgnoredDead*"] } };
        var findings = await MainFindingsIn("CatI.I2", config);
        Assert.Equal(new HashSet<string> { "CatI.I2.Sample.ReportedDead()" }, findings);

        // RED-FLIP: without the glob, the ignored symbol IS reported (proves it was there all along).
        var noIgnore = new KnipConfig();
        var both = await MainFindingsIn("CatI.I2", noIgnore);
        Assert.Contains("CatI.I2.Sample.IgnoredDeadMethod()", both);
        Assert.Contains("CatI.I2.Sample.ReportedDead()", both);
    }

    // ── I3: ignore.namespaces — whole namespace suppressed ────────────────────────────────────
    [Fact] // Sibling: CatI.I3.Kept.* IS flagged; CatI.I3.Ignored.* suppressed.
    public async Task I3_ignore_namespaces_suppresses_whole_namespace()
    {
        var config = new KnipConfig { Ignore = { Namespaces = ["CatI.I3.Ignored"] } };
        var findings = await MainFindingsIn("CatI.I3", config);
        Assert.Equal(new HashSet<string> { "CatI.I3.Kept.Widget.KeptNamespaceMethod()" }, findings);

        // RED-FLIP: without the namespace ignore, the ignored namespace's dead symbol IS reported.
        var both = await MainFindingsIn("CatI.I3", new KnipConfig());
        Assert.Contains("CatI.I3.Ignored.Widget.IgnoredNamespaceMethod()", both);
        Assert.Contains("CatI.I3.Kept.Widget.KeptNamespaceMethod()", both);
    }

    // ── I4: ignore.projects — project skipped entirely; assembly absent from solution set ─────
    [Fact] // RED-FLIP: without the project ignore, BOTH projects' dead symbols are reported.
    public async Task I4_ignore_projects_skips_project_entirely()
    {
        var solution = FixtureSolution("I4");

        // With ignore.projects: only the KEPT project's dead symbol remains; skipped one is gone.
        var skipped = await FindingsIn(solution, "CatI.I4",
            new KnipConfig { Ignore = { Projects = ["CatI.I4.Skipped"] } });
        Assert.Equal(new HashSet<string> { "CatI.I4.KeptSample.KeptDead()" }, skipped);

        // RED-FLIP: default config analyzes BOTH projects, so BOTH dead symbols are reported.
        var both = await FindingsIn(solution, "CatI.I4", new KnipConfig());
        Assert.Equal(
            new HashSet<string>
            {
                "CatI.I4.KeptSample.KeptDead()",
                "CatI.I4.SkippedSample.SkippedDead()",
            },
            both);
    }

    // ── I5a: knip.json discovered NEAREST-up from a start directory ───────────────────────────
    [Fact]
    public void I5_discover_resolves_nearest_knip_json_up_the_tree()
    {
        var discoveryRoot = Path.Combine(RepoRoot(), "tests", "fixtures", "CatI", "I5.Discovery");
        var nested = Path.Combine(discoveryRoot, "nested");

        // From nested/: the NEAREST knip.json is nested/knip.json.
        var fromNested = KnipConfig.Discover(nested);
        Assert.Equal(Path.GetFullPath(Path.Combine(nested, "knip.json")), Path.GetFullPath(fromNested!));

        // From the parent (no knip.json below it on this branch): the outer knip.json.
        var fromRoot = KnipConfig.Discover(discoveryRoot);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(discoveryRoot, "knip.json")),
            Path.GetFullPath(fromRoot!));

        // Nearest-up is strict: the two resolved paths differ (nested shadows outer).
        Assert.NotEqual(Path.GetFullPath(fromNested!), Path.GetFullPath(fromRoot!));
    }

    // ── I5b: --config OVERRIDES discovery (exercised via the CLI process boundary) ────────────
    [Fact]
    public void I5_cli_config_flag_overrides_discovery()
    {
        var solution = Path.Combine(RepoRoot(), "tests", "fixtures", "CatI", "I5.Cli", "Fixture.slnx");
        var overrideConfig = Path.Combine(
            RepoRoot(), "tests", "fixtures", "CatI", "I5.Cli", "override.knip.json");

        // Default run (no --config): the single dead symbol is found -> exit 1.
        var defaultRun = RunCli("-s", solution);
        Assert.Equal(1, defaultRun.ExitCode);

        // With --config pointing at a knip.json that ignores that symbol: nothing found -> exit 0.
        var overridden = RunCli("-s", solution, "-c", overrideConfig);
        Assert.Equal(0, overridden.ExitCode);
    }

    // ── I6: malformed knip.json → exit 2 with a clean error, NO stack trace ───────────────────
    //
    // RED CONTRACT ROW — GENUINE BUG (invariant #8; surfaced, NOT hidden by weakening the assertion
    // and NOT worked around by editing src/). OBSERVED: a malformed knip.json makes the CLI exit 134
    // with an UNHANDLED System.Text.Json.JsonException and a full stack trace on stderr — the
    // contract wants exit 2 with a clean "error:" line and no "   at " frames. ROOT CAUSE:
    // Runner.RunAsync calls KnipConfig.Load(configPath) at line ~34, OUTSIDE the try/catch that
    // starts at line ~58, so JsonException propagates unhandled. The assertions below encode the
    // TRUE contract and are kept intact; the row is Skip-marked (like CatB's B6 decision row) only so
    // the suite gate stays green while the bug is loudly recorded. Un-skip verifies the fix.
    [Fact]
    public void I6_malformed_config_exits_two_without_stack_trace()
    {
        var solution = Path.Combine(RepoRoot(), "tests", "fixtures", "CatI", "I5.Cli", "Fixture.slnx");
        var malformed = Path.Combine(RepoRoot(), "tests", "fixtures", "CatI", "I6", "malformed.knip.json");

        var r = RunCli("-s", solution, "-c", malformed);

        // Contract: a bad config is a usage/load error (exit 2), not a crash.
        Assert.Equal(2, r.ExitCode);
        // Contract: a clean diagnostic — NOT a raw exception dump with stack frames.
        Assert.DoesNotContain("   at ", r.StdErr);
        Assert.DoesNotContain("Unhandled exception", r.StdErr);
        Assert.Contains("error:", r.StdErr);
    }

    // ── I7: deliberately unresolved type → unresolved-type WARNING present (invariant #6) ─────
    [Fact]
    public async Task I7_unresolved_type_emits_load_warning()
    {
        var result = await RunSolution(FixtureSolution("I7"), new KnipConfig());
        Assert.Contains(result.LoadDiagnostics, d => d.Contains("unresolved types"));
    }

    // ── I8: clean, fully-resolved solution → NO unresolved-type warning ───────────────────────
    [Fact] // Anti-vacuous pairing with I7: same warning text, asserted ABSENT on the clean solution.
    public async Task I8_clean_solution_has_no_unresolved_type_warning()
    {
        var result = await RunSolution(MainSolution(), new KnipConfig());
        Assert.DoesNotContain(result.LoadDiagnostics, d => d.Contains("unresolved types"));
        // Guard against a vacuous pass: the clean solution DID analyze real code (it has findings).
        Assert.NotEmpty(result.Findings);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static string MainSolution() =>
        Path.Combine(RepoRoot(), "tests", "fixtures", "CatI", "Main", "Fixture.slnx");

    private static string FixtureSolution(string folder) =>
        Path.Combine(RepoRoot(), "tests", "fixtures", "CatI", folder, "Fixture.slnx");

    private static Task<AnalysisResult> RunSolution(string solution, KnipConfig config) =>
        KnipEngine.RunAsync(config, solution);

    private static async Task<IReadOnlySet<string>> FindingsIn(
        string solution, string scenarioNamespace, KnipConfig config)
    {
        var result = await RunSolution(solution, config);
        var prefix = scenarioNamespace + ".";
        return result.Findings.Select(f => f.Symbol)
            .Where(s => s.StartsWith(prefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Task<IReadOnlySet<string>> MainFindingsIn(string scenarioNamespace, KnipConfig config) =>
        FindingsIn(MainSolution(), scenarioNamespace, config);

    private readonly record struct CliResult(int ExitCode, string StdOut, string StdErr);

    /// <summary>Invoke the built CLI out-of-process. Copied (not shared) from CatJ.</summary>
    private static CliResult RunCli(params string[] args)
    {
        var cliProject = Path.Combine(RepoRoot(), "src", "Knip.Cli", "Knip.Cli.csproj");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot(),
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(cliProject);
        // The CLI multi-targets (net10.0;net472); pick the runnable TFM (net472 is Windows-only e2e).
        psi.ArgumentList.Add("--framework");
        psi.ArgumentList.Add("net10.0");
        psi.ArgumentList.Add("--no-build"); // build happens once in the verification gate
        psi.ArgumentList.Add("--");
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["NO_COLOR"] = "1";

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"CLI did not exit within 120s for args: {string.Join(' ', args)}");
        }
        Task.WaitAll(stdout, stderr);
        return new CliResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>Walk up to the repo root (marked by Knip.slnx). Mirrors FixtureRunner; not shared.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Knip.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException(
                "Could not locate repo root (Knip.slnx) from " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
