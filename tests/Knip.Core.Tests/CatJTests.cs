using System.Diagnostics;
using System.Text.Json;
using Json.Schema;
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

    [Fact]
    public void J1_help_and_version_are_standalone_queries()
    {
        using var temp = new TempDir();

        var bare = RunCliIn(temp.Path);
        Assert.Equal(0, bare.ExitCode);
        Assert.Contains("dotnet-knip analyze", bare.StdOut);
        Assert.Equal(string.Empty, bare.StdErr.Trim());

        foreach (var args in new[]
                 {
                     new[] { "analyze", "--help" },
                     new[] { "explain", "--help" },
                     new[] { "config", "--help" },
                     new[] { "config", "doctor", "--help" },
                 })
        {
            var help = RunCliIn(temp.Path, args);
            Assert.Equal(0, help.ExitCode);
            Assert.Contains("Examples", help.StdOut);
            Assert.Equal(string.Empty, help.StdErr.Trim());
        }

        var version = RunCliIn(temp.Path, "--version");
        Assert.Equal(0, version.ExitCode);
        Assert.Equal("Knip.NET 0.1.0-preview.1", version.StdOut.Trim());
        Assert.Equal(string.Empty, version.StdErr.Trim());
    }

    [Fact]
    public void J2_analysis_exit_codes_and_discovery_are_stable()
    {
        Assert.Equal(0, RunCli("analyze", CleanSolution).ExitCode);
        Assert.Equal(1, RunCli("analyze", WithFindingsSolution).ExitCode);
        Assert.Equal(0, RunCli("analyze", WithFindingsSolution, "--no-fail").ExitCode);

        var discovered = RunCliIn(
            Path.GetDirectoryName(WithFindingsSolution)!, "analyze", "--no-fail");
        Assert.Equal(0, discovered.ExitCode);
    }

    [Fact]
    public void J3_invalid_and_removed_syntax_fails_before_file_access()
    {
        var cases = new[]
        {
            new[] { "--definitely-not-an-option" },
            new[] { "-s", WithFindingsSolution },
            new[] { WithFindingsSolution },
            new[] { "analyze", WithFindingsSolution, "extra.slnx" },
            new[] { "analyze", WithFindingsSolution, "--format", "json", "--format", "sarif" },
            new[] { "analyze", WithFindingsSolution, "--symbol", "Anything" },
            new[] { "analyze", WithFindingsSolution, "--why", "Anything" },
            new[] { "analyze", WithFindingsSolution, "--print-config" },
            new[] { "--agent-instructions" },
            new[] { "init", "--agent" },
            new[] { "analyze", "--help", WithFindingsSolution },
            new[] { "--version", WithFindingsSolution },
        };

        foreach (var args in cases)
        {
            var result = RunCli(args);
            Assert.Equal(2, result.ExitCode);
            Assert.Equal(string.Empty, result.StdOut.Trim());
            Assert.Contains("error:", result.StdErr);
            Assert.Contains("--help", result.StdErr);
        }
    }

    [Fact]
    public void J4_target_errors_are_explicit_and_ambiguous_candidates_are_sorted()
    {
        using var temp = new TempDir();
        var missing = RunCliIn(temp.Path, "analyze", "Missing.slnx");
        Assert.Equal(2, missing.ExitCode);
        Assert.Contains("target not found", missing.StdErr);

        File.WriteAllText(Path.Combine(temp.Path, "input.txt"), "text");
        var unsupported = RunCliIn(temp.Path, "analyze", "input.txt");
        Assert.Equal(2, unsupported.ExitCode);
        Assert.Contains("unsupported target", unsupported.StdErr);

        File.WriteAllText(Path.Combine(temp.Path, "B.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(temp.Path, "A.slnx"), "<Solution />");
        var ambiguous = RunCliIn(temp.Path, "analyze");
        Assert.Equal(2, ambiguous.ExitCode);
        Assert.True(ambiguous.StdErr.IndexOf("A.slnx", StringComparison.Ordinal) <
                    ambiguous.StdErr.IndexOf("B.slnx", StringComparison.Ordinal));
    }

    [Fact]
    public void J5_json_is_v2_stable_and_uses_the_product_version()
    {
        var first = RunCli("analyze", WithFindingsSolution, "--format", "json");
        Assert.Equal(1, first.ExitCode);
        using var document = JsonDocument.Parse(first.StdOut);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("Knip.NET", root.GetProperty("tool").GetProperty("name").GetString());
        Assert.Equal("0.1.0-preview.1", root.GetProperty("tool").GetProperty("version").GetString());

        var findings = root.GetProperty("findings").EnumerateArray().ToList();
        Assert.NotEmpty(findings);
        Assert.All(findings, finding =>
        {
            if (finding.GetProperty("symbol").GetString() == "CatJ.WithFindings.Program.UnusedAlpha()")
                Assert.False(finding.TryGetProperty("rootCause", out _));
        });

        var expected = FindingKeys(first.StdOut)
            .OrderBy(key => key.Project, StringComparer.Ordinal)
            .ThenBy(key => key.File, StringComparer.Ordinal)
            .ThenBy(key => key.Line)
            .ToList();
        Assert.Equal(expected, FindingKeys(first.StdOut));
        Assert.Equal(FindingKeys(first.StdOut), FindingKeys(
            RunCli("analyze", WithFindingsSolution, "--format", "json").StdOut));
    }

    [Fact]
    public void J6_verbose_json_keeps_stdout_pure_and_writes_progress_to_stderr()
    {
        var result = RunCli(
            "analyze", WithFindingsSolution, "--format", "json", "--verbose", "--no-fail");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StdOut);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Contains("Loading", result.StdErr);
    }

    [Fact]
    public void J7_sarif_carries_stable_ids_safety_properties_and_relative_paths()
    {
        var sarif = RunCli("analyze", WithFindingsSolution, "--format", "sarif");
        var json = RunCli("analyze", WithFindingsSolution, "--format", "json");
        Assert.Equal(1, sarif.ExitCode);

        using var sarifDocument = JsonDocument.Parse(sarif.StdOut);
        using var jsonDocument = JsonDocument.Parse(json.StdOut);

        // The vendored OASIS schema declares draft 4. JsonSchema.Net starts at draft 6, so adapt only
        // the dialect and identifier keywords before evaluating the unchanged SARIF definitions.
        var schemaText = File.ReadAllText(Path.Combine(
            RepoRoot(), "tests", "schemas", "sarif-schema-2.1.0.json"))
            .Replace("http://json-schema.org/draft-04/schema#", "http://json-schema.org/draft-07/schema#", StringComparison.Ordinal)
            .Replace("\"id\": \"https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json\"",
                "\"$id\": \"https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json\"",
                StringComparison.Ordinal);
        var schema = JsonSchema.FromText(schemaText);
        var evaluation = schema.Evaluate(
            sarifDocument.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(
            evaluation.IsValid,
            string.Join(Environment.NewLine, evaluation.Details
                .Where(detail => detail.HasErrors)
                .SelectMany(detail => detail.Errors!.Select(error =>
                    $"{detail.InstanceLocation}: {error.Value}"))));
        var run = sarifDocument.RootElement.GetProperty("runs")[0];
        Assert.Equal("2.1.0", sarifDocument.RootElement.GetProperty("version").GetString());
        Assert.Equal("0.1.0-preview.1", run.GetProperty("tool").GetProperty("driver").GetProperty("version").GetString());
        Assert.Equal(
            jsonDocument.RootElement.GetProperty("findings").GetArrayLength(),
            run.GetProperty("results").GetArrayLength());

        foreach (var result in run.GetProperty("results").EnumerateArray())
        {
            Assert.Matches("^[a-z]", result.GetProperty("ruleId").GetString()!);
            Assert.Equal("warning", result.GetProperty("level").GetString());
            Assert.StartsWith("k1_", result.GetProperty("partialFingerprints").GetProperty("knipId/v1").GetString());
            Assert.True(result.GetProperty("properties").TryGetProperty("confidence", out _));
            var uri = result.GetProperty("locations")[0].GetProperty("physicalLocation")
                .GetProperty("artifactLocation").GetProperty("uri").GetString()!;
            Assert.False(Path.IsPathRooted(uri));
        }
    }

    [Fact]
    public void J8_console_report_contains_deletion_safety_fields_and_ascii_only()
    {
        var result = RunCli("analyze", WithFindingsSolution);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("confidence:", result.StdOut);
        Assert.Contains("remediation:", result.StdOut);
        Assert.Contains("reliability:", result.StdOut);
        Assert.Contains("k1_", result.StdOut);
        Assert.All(result.StdOut, character => Assert.True(character <= 127));
        Assert.Equal(string.Empty, result.StdErr.Trim());
    }

    [Fact]
    public void J9_config_doctor_is_offline_strict_and_read_only()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "knip.json");
        File.WriteAllText(path, "{ // comment\n \"production\": false,\n}");
        var before = File.ReadAllBytes(path);

        var valid = RunCliIn(temp.Path, "config", "doctor");
        Assert.Equal(0, valid.ExitCode);
        Assert.EndsWith("/knip.json", valid.StdOut.Trim().Replace('\\', '/'));
        Assert.Equal(before, File.ReadAllBytes(path));

        foreach (var invalid in new[]
                 {
                     "{ \"Production\": true }",
                     "{ \"production\": \"yes\" }",
                     "{ \"solution\": \"App.slnx\" }",
                     "{ \"output\": { \"format\": \"json\" } }",
                     "{ \"plugins\": { \"missing\": { \"enabled\": true } } }",
                     "{ \"plugins\": { \"reflection\": { \"aliases\": {} } } }",
                 })
        {
            File.WriteAllText(path, invalid);
            var failed = RunCliIn(temp.Path, "config", "doctor");
            Assert.Equal(2, failed.ExitCode);
            Assert.Equal(string.Empty, failed.StdOut.Trim());
            Assert.Contains("invalid config", failed.StdErr);
        }
    }

    [Fact]
    public void J10_missing_config_and_explanation_errors_leave_stdout_empty()
    {
        var missingConfig = RunCli(
            "analyze", WithFindingsSolution, "--config", "missing-knip.json", "--no-fail");
        Assert.Equal(2, missingConfig.ExitCode);
        Assert.Equal(string.Empty, missingConfig.StdOut.Trim());

        var missingSymbol = RunCli(
            "explain", WithFindingsSolution, "--symbol", "CatJ.WithFindings.DoesNotExist");
        Assert.Equal(2, missingSymbol.ExitCode);
        Assert.Equal(string.Empty, missingSymbol.StdOut.Trim());
        Assert.Contains("no symbol matches", missingSymbol.StdErr);

        var projectTarget = Path.Combine(RepoRoot(), "tests", "fixtures", "WS2", "Fixture.slnx");
        var projectJson = RunCli("analyze", projectTarget, "--format", "json", "--no-fail");
        using var document = JsonDocument.Parse(projectJson.StdOut);
        var projectReferenceId = document.RootElement.GetProperty("findings").EnumerateArray()
            .Single(finding => finding.GetProperty("kind").GetString() == "unusedProjectReference")
            .GetProperty("id").GetString()!;
        var unsupported = RunCli("explain", projectTarget, "--symbol", projectReferenceId);
        Assert.Equal(2, unsupported.ExitCode);
        Assert.Equal(string.Empty, unsupported.StdOut.Trim());
        Assert.Contains("not a graph symbol", unsupported.StdErr);
    }

    [Fact]
    public void J11_analysis_does_not_modify_target_or_config()
    {
        var source = Path.Combine(Path.GetDirectoryName(WithFindingsSolution)!, "Program.cs");
        var config = Path.Combine(RepoRoot(), "knip.json");
        var sourceBefore = File.ReadAllBytes(source);
        var configBefore = File.ReadAllBytes(config);

        Assert.Equal(0, RunCli(
            "analyze", WithFindingsSolution, "--format", "json", "--no-fail").ExitCode);
        Assert.Equal(sourceBefore, File.ReadAllBytes(source));
        Assert.Equal(configBefore, File.ReadAllBytes(config));
    }

    [Fact]
    public void J12_explain_reports_code_reachable_only_from_tests()
    {
        using var temp = new TempDir();
        var config = Path.Combine(temp.Path, "knip.json");
        File.WriteAllText(config,
            "{ \"entryPoints\": { \"symbolNames\": [\"Main\"], \"attributes\": [\"Fact\"] } }");
        var target = Path.Combine(RepoRoot(), "tests", "fixtures", "CatK", "Fixture.slnx");

        var result = RunCli(
            "explain", target,
            "--symbol", "CatK.K5.Chain.B()",
            "--production",
            "--config", config);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status: only-used-by-tests", result.StdOut);
        Assert.Contains("production root path: none", result.StdOut);
        Assert.Contains("test root path:", result.StdOut);
    }

    // Helpers
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "knip-cli-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private readonly record struct FindingKey(string Project, string File, int Line);

    private static IReadOnlyList<FindingKey> FindingKeys(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("findings").EnumerateArray()
            .Select(f => new FindingKey(
                f.GetProperty("project").GetString() ?? "",
                f.GetProperty("location").GetProperty("file").GetString() ?? "",
                f.GetProperty("location").GetProperty("line").GetInt32()))
            .ToList();
    }

    private readonly record struct CliResult(int ExitCode, string StdOut, string StdErr);

    private static CliResult RunCli(params string[] args) => RunCliIn(RepoRoot(), args);

    /// <summary>Invoke the built CLI with an explicit working directory.</summary>
    private static CliResult RunCliIn(string workingDirectory, params string[] args)
    {
        var cliProject = Path.Combine(RepoRoot(), "src", "Knip.Cli", "Knip.Cli.csproj");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(cliProject);
        // The CLI multi-targets (net10.0;net472); pick the runnable TFM. net472 exists only so the
        // engine can build against full-framework MSBuild for legacy solutions (Windows-only e2e).
        psi.ArgumentList.Add("--framework");
        psi.ArgumentList.Add("net10.0");
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
