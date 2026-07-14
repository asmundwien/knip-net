using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category J — CLI contract. These tests invoke the BUILT dotnet-knip CLI as a SEPARATE process
/// and assert on the observable contract: exit code (0 clean / 1 findings / 2 error), the
/// stdout/stderr split (machine output on stdout, progress/diagnostics on stderr), and the shape
/// of the json/sarif payloads. Nothing here touches the in-proc engine — the point of category J
/// is to exercise the real process boundary. All rows are Contract: they must be GREEN.
///
/// NOTE: no [Collection(MsBuildCollection.Name)] here. The CLI runs out-of-process and registers
/// its own MSBuild via MSBuildLocator; this test process never loads a workspace, so it does not
/// need (and must not share) the in-proc MSBuild collection fixture.
/// </summary>
public sealed class CatJTests
{
    private static readonly string CleanSolution =
        FixtureSolution("Clean");
    private static readonly string WithFindingsSolution =
        FixtureSolution("WithFindings");

    // ── J1: clean solution → exit 0 ──────────────────────────────────────────────────────────
    [Fact]
    public void J1_clean_solution_exits_zero()
    {
        var r = RunCli("-s", CleanSolution);
        Assert.Equal(0, r.ExitCode);
    }

    // ── J2: findings → exit 1; with --no-fail → exit 0 ───────────────────────────────────────
    [Fact]
    public void J2_findings_exit_one_and_no_fail_exit_zero()
    {
        var withFail = RunCli("-s", WithFindingsSolution);
        Assert.Equal(1, withFail.ExitCode);

        var noFail = RunCli("-s", WithFindingsSolution, "--no-fail");
        Assert.Equal(0, noFail.ExitCode);
    }

    // ── J3: bad args / missing target → exit 2 + usage on STDERR ──────────────────────────────
    [Fact]
    public void J3_bad_option_exits_two_with_usage_on_stderr()
    {
        var r = RunCli("--definitely-not-an-option");
        Assert.Equal(2, r.ExitCode);
        // Usage text goes to stderr; stdout stays clean.
        Assert.Contains("Usage:", r.StdErr);
        Assert.Contains("dotnet-knip", r.StdErr);
        Assert.Equal(string.Empty, r.StdOut.Trim());
    }

    [Fact]
    public void J3_missing_target_exits_two_on_stderr()
    {
        var missing = Path.Combine(Path.GetDirectoryName(WithFindingsSolution)!, "DoesNotExist.slnx");
        var r = RunCli("-s", missing);
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("error:", r.StdErr);
        Assert.Equal(string.Empty, r.StdOut.Trim());
    }

    // ── J4: --format json parses; findings sorted project→file→line, stable across runs ───────
    [Fact]
    public void J4_json_parses_and_is_sorted_and_stable()
    {
        var first = RunCli("-s", WithFindingsSolution, "-f", "json");
        Assert.Equal(1, first.ExitCode);

        var order1 = FindingKeys(first.StdOut);
        Assert.NotEmpty(order1);

        // project → file → line ordering (ordinal on project/file, numeric on line).
        var expected = order1
            .OrderBy(k => k.Project, StringComparer.Ordinal)
            .ThenBy(k => k.File, StringComparer.Ordinal)
            .ThenBy(k => k.Line)
            .ToList();
        Assert.Equal(expected, order1);

        // Stability: a second identical run yields the identical ordering.
        var second = RunCli("-s", WithFindingsSolution, "-f", "json");
        var order2 = FindingKeys(second.StdOut);
        Assert.Equal(order1, order2);
    }

    // ── J5: --format sarif → valid minimal SARIF 2.1.0 with one located result per finding ────
    [Fact]
    public void J5_sarif_is_valid_minimal_2_1_0()
    {
        var r = RunCli("-s", WithFindingsSolution, "-f", "sarif");
        Assert.Equal(1, r.ExitCode);

        using var doc = JsonDocument.Parse(r.StdOut);
        var root = doc.RootElement;

        // version must be exactly "2.1.0".
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());

        // A schema URI must be present under the SARIF 2.1.0 spec name "$schema" (GitHub
        // code-scanning keys on it).
        Assert.True(
            root.TryGetProperty("$schema", out _),
            "SARIF output is missing the \"$schema\" pointer required by SARIF 2.1.0.");

        var runs = root.GetProperty("runs");
        Assert.Equal(1, runs.GetArrayLength());
        var run = runs[0];

        // tool.driver.name present and non-empty.
        var name = run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString();
        Assert.False(string.IsNullOrWhiteSpace(name));

        // One result per finding, each with a physicalLocation.
        var results = run.GetProperty("results");
        Assert.True(results.GetArrayLength() >= 1);
        foreach (var result in results.EnumerateArray())
        {
            var locations = result.GetProperty("locations");
            Assert.True(locations.GetArrayLength() >= 1);
            Assert.True(
                locations[0].TryGetProperty("physicalLocation", out _),
                "SARIF result location has no physicalLocation.");
        }

        // Cross-check: result count matches the json finding count for the same fixture.
        var json = RunCli("-s", WithFindingsSolution, "-f", "json");
        Assert.Equal(FindingKeys(json.StdOut).Count, results.GetArrayLength());
    }

    // ── J6: machine output on stdout stays uncorrupted; -v progress goes to stderr ────────────
    [Fact]
    public void J6_verbose_progress_on_stderr_stdout_stays_pure_json()
    {
        var r = RunCli("-s", WithFindingsSolution, "-f", "json", "-v");
        Assert.Equal(1, r.ExitCode);

        // stdout must parse as JSON despite -v: no progress line leaked in.
        using var doc = JsonDocument.Parse(r.StdOut);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);

        // Progress must have gone to stderr.
        Assert.False(string.IsNullOrWhiteSpace(r.StdErr), "-v produced no progress on stderr.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private readonly record struct FindingKey(string Project, string File, int Line);

    private static IReadOnlyList<FindingKey> FindingKeys(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("findings").EnumerateArray()
            .Select(f => new FindingKey(
                f.GetProperty("project").GetString() ?? "",
                f.GetProperty("filePath").GetString() ?? "",
                f.GetProperty("line").GetInt32()))
            .ToList();
    }

    private readonly record struct CliResult(int ExitCode, string StdOut, string StdErr);

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
        psi.ArgumentList.Add("--no-build"); // build happens once in the verification gate
        psi.ArgumentList.Add("--");
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Keep output deterministic (no ANSI colour) regardless of the host terminal.
        psi.Environment["NO_COLOR"] = "1";

        using var process = new Process { StartInfo = psi };
        process.Start();
        // Read both streams to completion before waiting, to avoid pipe-buffer deadlock.
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

    private static string FixtureSolution(string variant) =>
        Path.Combine(RepoRoot(), "tests", "fixtures", "CatJ", variant, "Fixture.slnx");

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
