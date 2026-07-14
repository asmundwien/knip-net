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

/// <summary>Machine-readable JSON for CI pipelines and other tools.</summary>
public sealed class JsonReporter : IReporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Report(AnalysisResult result, TextWriter output)
    {
        var payload = new
        {
            summary = new
            {
                projectsAnalyzed = result.ProjectsAnalyzed,
                symbolsAnalyzed = result.SymbolsAnalyzed,
                roots = result.RootCount,
                unused = result.Findings.Count,
                elapsedSeconds = Math.Round(result.Elapsed.TotalSeconds, 2),
            },
            loadDiagnostics = result.LoadDiagnostics,
            findings = result.Findings,
        };
        output.WriteLine(JsonSerializer.Serialize(payload, Options));
    }
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
            message = new { text = $"Unused {f.SymbolKind} '{f.Symbol}' is never referenced." },
            locations = new[]
            {
                new
                {
                    physicalLocation = new
                    {
                        artifactLocation = new { uri = new Uri(f.FilePath).AbsoluteUri },
                        region = new { startLine = f.Line, startColumn = f.Column },
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
