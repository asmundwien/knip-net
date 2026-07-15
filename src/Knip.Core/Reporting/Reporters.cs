using System.Text.Json;
using System.Text.Json.Serialization;
using Knip.Core.Model;

namespace Knip.Core.Reporting;

public interface IReporter
{
    void Report(AnalysisResult result, TextWriter output);
}

public static class ReporterFactory
{
    public static IReporter Create(string format) => format.ToLowerInvariant() switch
    {
        "json" => new JsonReporter(),
        "sarif" => new SarifReporter(),
        _ => new ConsoleReporter(),
    };
}

/// <summary>Human-readable, grouped-by-project console output with ANSI colour.</summary>
public sealed class ConsoleReporter : IReporter
{
    private const string Reset = "[0m";
    private const string Bold = "[1m";
    private const string Dim = "[2m";
    private const string Red = "[31m";
    private const string Yellow = "[33m";
    private const string Cyan = "[36m";
    private const string Green = "[32m";

    private readonly bool _color = !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public void Report(AnalysisResult result, TextWriter output)
    {
        foreach (var diagnostic in result.LoadDiagnostics.Take(10))
            output.WriteLine($"{C(Yellow)}warning:{C(Reset)} {diagnostic}");
        if (result.LoadDiagnostics.Count > 10)
            output.WriteLine($"{C(Dim)}… and {result.LoadDiagnostics.Count - 10} more load warnings{C(Reset)}");

        if (result.Findings.Count == 0)
        {
            output.WriteLine($"{C(Green)}✓ No unused code found.{C(Reset)}");
            WriteSummary(result, output);
            return;
        }

        foreach (var group in result.Findings.GroupBy(f => f.Project).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            output.WriteLine();
            output.WriteLine($"{C(Bold)}{group.Key}{C(Reset)} {C(Dim)}({group.Count()} unused){C(Reset)}");
            foreach (var f in group)
            {
                if (f.Kind is FindingKind.UnusedProjectReference or FindingKind.UnusedPackageReference)
                {
                    output.WriteLine(
                        $"  {C(Red)}{f.SymbolKind,-10}{C(Reset)} {C(Cyan)}{f.ReferencedProject ?? f.Symbol}{C(Reset)}");
                    output.WriteLine(
                        $"             {C(Dim)}unused reference · {RelativePath(f.FilePath)}{C(Reset)}");
                    continue;
                }

                var location = $"{RelativePath(f.FilePath)}:{f.Line}";
                output.WriteLine(
                    $"  {C(Red)}{f.SymbolKind,-10}{C(Reset)} {C(Cyan)}{f.Symbol}{C(Reset)}");
                output.WriteLine(
                    $"             {C(Dim)}{f.Accessibility} · {location}{C(Reset)}");
            }
        }

        WriteSummary(result, output);
    }

    private void WriteSummary(AnalysisResult result, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine(
            $"{C(Dim)}Analyzed {result.ProjectsAnalyzed} project(s), {result.SymbolsAnalyzed} symbols, " +
            $"{result.RootCount} roots in {result.Elapsed.TotalSeconds:0.0}s{C(Reset)}");
        if (result.Findings.Count > 0)
            output.WriteLine($"{C(Bold)}{C(Red)}{result.Findings.Count} unused symbol(s).{C(Reset)}");
    }

    private static string RelativePath(string path)
    {
        var cwd = Directory.GetCurrentDirectory();
        return path.StartsWith(cwd, StringComparison.OrdinalIgnoreCase)
            ? path[(cwd.Length + 1)..]
            : path;
    }

    private string C(string code) => _color ? code : string.Empty;
}

/// <summary>
/// Machine-readable JSON v2 — the product API (WS8 §1). Root: formatVersion, tool, run, reliability,
/// summary, findings[]. BREAKING change from v1 (single shape; formatVersion lets consumers fail fast).
/// </summary>
public sealed class JsonReporter : IReporter
{
    private const int FormatVersion = 2;
    private const string ToolName = "Knip.NET";
    private const string ToolVersion = "0.1.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Report(AnalysisResult result, TextWriter output)
    {
        var payload = new
        {
            formatVersion = FormatVersion,
            tool = new { name = ToolName, version = ToolVersion },
            run = new
            {
                projectsAnalyzed = result.ProjectsAnalyzed,
                symbolsAnalyzed = result.SymbolsAnalyzed,
                roots = result.RootCount,
                elapsedSeconds = Math.Round(result.Elapsed.TotalSeconds, 2),
            },
            reliability = new
            {
                degraded = result.Reliability.Degraded,
                projectsLoaded = result.Reliability.ProjectsLoaded,
                projectsFailed = result.Reliability.ProjectsFailed
                    .Select(p => new { project = p.Project, message = p.Message }).ToList(),
                unresolvedTypeReferences = result.Reliability.UnresolvedTypeReferences,
                restoreFailures = result.Reliability.RestoreFailures,
                loadDiagnostics = result.Reliability.LoadDiagnostics
                    .Select(d => new { severity = Camel(d.Severity.ToString()), code = d.Code, message = d.Message })
                    .ToList(),
                // WS7: surfaced (does NOT set degraded — changes finding MEANING, not graph trust).
                productionModeWarnings = result.Reliability.ProductionModeWarnings,
                testProjectClassification = result.Reliability.TestProjectClassifications
                    .Select(c => new { project = c.Project, kind = c.Kind, signal = c.Signal })
                    .ToList(),
            },
            summary = BuildSummary(result.Findings),
            findings = result.Findings.Select(ToJson).ToList(),
        };
        output.WriteLine(JsonSerializer.Serialize(payload, Options));
    }

    private static object BuildSummary(IReadOnlyList<Finding> findings) => new
    {
        total = findings.Count,
        byKind = CountBy(findings, f => Camel(f.Kind.ToString())),
        byConfidence = CountBy(findings, f => Camel(f.Confidence.ToString())),
        byProject = findings
            .GroupBy(f => f.Project, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                project = g.Key,
                total = g.Count(),
                byKind = CountBy(g, f => Camel(f.Kind.ToString())),
                byConfidence = CountBy(g, f => Camel(f.Confidence.ToString())),
            })
            .ToList(),
    };

    private static Dictionary<string, int> CountBy(IEnumerable<Finding> findings, Func<Finding, string> key)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in findings)
        {
            var k = key(f);
            counts[k] = counts.TryGetValue(k, out var n) ? n + 1 : 1;
        }
        return counts;
    }

    private static object ToJson(Finding f) => new
    {
        id = f.Id,
        kind = Camel(f.Kind.ToString()),
        symbol = f.Symbol,
        symbolKind = f.SymbolKind,
        accessibility = f.Accessibility,
        project = f.Project,
        confidence = Camel(f.Confidence.ToString()),
        hazards = f.Hazards.Select(h => Camel(h.ToString())).ToList(),
        remediation = Camel(f.Remediation.ToString()),
        location = new { file = f.FilePath, line = f.Line, column = f.Column },
        span = f.Span is null ? null : new
        {
            file = f.Span.File,
            start = new { line = f.Span.Start.Line, column = f.Span.Start.Column },
            end = new { line = f.Span.End.Line, column = f.Span.End.Column },
        },
        referencedProject = f.ReferencedProject,
        rootCause = f.RootCause,
        // WS7: OnlyUsedByTests carries its referencing test symbols under details.testReferrers (K3), so
        // the deletion unit — code AND its tests — is visible. Empty object for every other kind.
        details = f.TestReferrers.Count == 0
            ? (object)new { }
            : new { testReferrers = f.TestReferrers.Select(r => new { symbol = r.Symbol, file = r.File, line = r.Line }).ToList() },
    };

    private static string Camel(string pascal) =>
        pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
}

/// <summary>Minimal SARIF 2.1.0 so findings surface as annotations in GitHub/Azure DevOps.</summary>
public sealed class SarifReporter : IReporter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Report(AnalysisResult result, TextWriter output)
    {
        var results = result.Findings.Select(f => new
        {
            ruleId = f.Kind.ToString(),
            level = "warning",
            message = new
            {
                text = f.Kind switch
                {
                    FindingKind.UnusedProjectReference =>
                        $"Project '{f.Project}' references '{f.ReferencedProject ?? f.Symbol}' but uses no type from it.",
                    FindingKind.UnusedPackageReference =>
                        $"Project '{f.Project}' references package '{f.Symbol}' but uses no type from it.",
                    _ => $"Unused {f.SymbolKind} '{f.Symbol}' is never referenced.",
                },
            },
            // Stable content-hash id (WS8 §3.2 / §5.5): lets code-scanning platforms dedupe/track a
            // finding across commits. Same value the JSON v2 output publishes as `id`.
            partialFingerprints = new Dictionary<string, string> { ["knipId/v1"] = f.Id },
            locations = new[]
            {
                new
                {
                    physicalLocation = new
                    {
                        artifactLocation = new { uri = new Uri(f.FilePath).AbsoluteUri },
                        // SARIF regions are 1-based; omit the region for findings without a line
                        // (e.g. project references point at the .csproj as a whole).
                        region = f.Line > 0
                            ? (object)new { startLine = f.Line, startColumn = f.Column }
                            : null,
                    },
                },
            },
        });

        var sarif = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new[]
            {
                new
                {
                    tool = new { driver = new { name = "Knip.NET", version = "0.1.0" } },
                    results,
                },
            },
        };
        output.WriteLine(JsonSerializer.Serialize(sarif, Options));
    }
}
