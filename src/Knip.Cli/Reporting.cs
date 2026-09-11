using System.Text.Json;
using System.Text.Json.Serialization;
using Knip.Core.Model;

namespace Knip.Cli;

internal static class Reporting
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Write(ReportFormat format, AnalysisResult result, string targetPath, TextWriter output)
    {
        switch (format)
        {
            case ReportFormat.Console: WriteConsole(result, targetPath, output); break;
            case ReportFormat.Json: WriteJson(result, output); break;
            case ReportFormat.Sarif: WriteSarif(result, targetPath, output); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void WriteConsole(AnalysisResult result, string targetPath, TextWriter output)
    {
        foreach (var group in result.Findings.GroupBy(finding => finding.Project)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"project: {group.Key}");
            foreach (var finding in group)
            {
                output.WriteLine($"  {finding.Id}");
                output.WriteLine($"    confidence: {Camel(finding.Confidence.ToString())}");
                output.WriteLine($"    kind: {Camel(finding.Kind.ToString())}");
                output.WriteLine(
                    finding.Kind is FindingKind.UnusedProjectReference or FindingKind.UnusedPackageReference
                        ? $"    reference: {finding.ReferencedProject ?? finding.Symbol}"
                        : $"    symbol: {finding.Symbol}");
                output.WriteLine(
                    $"    location: {TargetRelativePath(targetPath, finding.FilePath)}:{finding.Line}:{finding.Column}");
                output.WriteLine($"    remediation: {Camel(finding.Remediation.ToString())}");
                if (finding.Hazards.Count > 0)
                    output.WriteLine($"    hazards: {string.Join(", ", finding.Hazards.Select(h => Camel(h.ToString())))}");
                if (finding.RootCause is not null)
                    output.WriteLine($"    covered by {finding.RootCause}");
            }
        }

        output.WriteLine($"reliability: {(result.Reliability.Degraded ? "degraded" : "complete")}");
        output.WriteLine($"projects analyzed: {result.ProjectsAnalyzed}");
        output.WriteLine($"symbols analyzed: {result.SymbolsAnalyzed}");
        output.WriteLine($"roots: {result.RootCount}");
        output.WriteLine($"findings: {result.Findings.Count}");
    }

    private static void WriteJson(AnalysisResult result, TextWriter output)
    {
        var payload = new
        {
            formatVersion = 2,
            tool = new { name = ProductInfo.Name, version = ProductInfo.Version },
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
                    .Select(project => new { project = project.Project, message = project.Message }).ToList(),
                unresolvedTypeReferences = result.Reliability.UnresolvedTypeReferences,
                restoreFailures = result.Reliability.RestoreFailures,
                loadDiagnostics = result.Reliability.LoadDiagnostics
                    .Select(diagnostic => new
                    {
                        severity = Camel(diagnostic.Severity.ToString()),
                        code = diagnostic.Code,
                        message = diagnostic.Message,
                    }).ToList(),
                productionModeWarnings = result.Reliability.ProductionModeWarnings,
                testProjectClassification = result.Reliability.TestProjectClassifications
                    .Select(item => new { project = item.Project, kind = item.Kind, signal = item.Signal }).ToList(),
            },
            summary = BuildSummary(result.Findings),
            findings = result.Findings.Select(ToJsonFinding).ToList(),
        };
        output.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static object BuildSummary(IReadOnlyList<Finding> findings) => new
    {
        total = findings.Count,
        byKind = CountBy(findings, finding => Camel(finding.Kind.ToString())),
        byConfidence = CountBy(findings, finding => Camel(finding.Confidence.ToString())),
        byProject = findings.GroupBy(finding => finding.Project, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                project = group.Key,
                total = group.Count(),
                byKind = CountBy(group, finding => Camel(finding.Kind.ToString())),
                byConfidence = CountBy(group, finding => Camel(finding.Confidence.ToString())),
            }).ToList(),
    };

    private static Dictionary<string, int> CountBy(
        IEnumerable<Finding> findings, Func<Finding, string> keySelector)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            var key = keySelector(finding);
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }
        return counts;
    }

    private static object ToJsonFinding(Finding finding) => new
    {
        id = finding.Id,
        kind = Camel(finding.Kind.ToString()),
        symbol = finding.Symbol,
        symbolKind = finding.SymbolKind,
        accessibility = finding.Accessibility,
        project = finding.Project,
        confidence = Camel(finding.Confidence.ToString()),
        hazards = finding.Hazards.Select(hazard => Camel(hazard.ToString())).ToList(),
        remediation = Camel(finding.Remediation.ToString()),
        location = new { file = finding.FilePath, line = finding.Line, column = finding.Column },
        span = finding.Span is null ? null : new
        {
            file = finding.Span.File,
            start = new { line = finding.Span.Start.Line, column = finding.Span.Start.Column },
            end = new { line = finding.Span.End.Line, column = finding.Span.End.Column },
        },
        referencedProject = finding.ReferencedProject,
        rootCause = finding.RootCause,
        details = finding.TestReferrers.Count == 0
            ? (object)new { }
            : new
            {
                testReferrers = finding.TestReferrers
                    .Select(item => new { symbol = item.Symbol, file = item.File, line = item.Line }).ToList(),
            },
    };

    private static void WriteSarif(AnalysisResult result, string targetPath, TextWriter output)
    {
        var results = result.Findings.Select(finding => ToSarifResult(finding, targetPath)).ToList();
        var rules = result.Findings.Select(finding => finding.Kind).Distinct()
            .OrderBy(kind => kind.ToString(), StringComparer.Ordinal)
            .Select(kind => new
            {
                id = Camel(kind.ToString()),
                name = kind.ToString(),
                shortDescription = new { text = DescribeKind(kind) },
            }).ToList();

        var payload = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = ProductInfo.Name,
                            version = ProductInfo.Version,
                            informationUri = "https://github.com/asmundwien/knip-net",
                            rules,
                        },
                    },
                    results,
                    properties = new
                    {
                        reliability = new
                        {
                            degraded = result.Reliability.Degraded,
                            projectsLoaded = result.Reliability.ProjectsLoaded,
                            projectsFailed = result.Reliability.ProjectsFailed
                                .Select(project => new { project = project.Project, message = project.Message }).ToList(),
                            unresolvedTypeReferences = result.Reliability.UnresolvedTypeReferences,
                            restoreFailures = result.Reliability.RestoreFailures,
                            loadDiagnostics = result.Reliability.LoadDiagnostics.Select(diagnostic => new
                            {
                                severity = Camel(diagnostic.Severity.ToString()),
                                code = diagnostic.Code,
                                message = diagnostic.Message,
                            }).ToList(),
                            productionModeWarnings = result.Reliability.ProductionModeWarnings,
                            testProjectClassification = result.Reliability.TestProjectClassifications
                                .Select(item => new { project = item.Project, kind = item.Kind, signal = item.Signal }).ToList(),
                        },
                    },
                },
            },
        };
        output.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static object ToSarifResult(Finding finding, string targetPath)
    {
        var properties = new Dictionary<string, object?>
        {
            ["id"] = finding.Id,
            ["confidence"] = Camel(finding.Confidence.ToString()),
            ["hazards"] = finding.Hazards.Select(hazard => Camel(hazard.ToString())).ToList(),
            ["remediation"] = Camel(finding.Remediation.ToString()),
        };
        if (finding.RootCause is not null)
            properties["rootCause"] = finding.RootCause;
        if (finding.Span is not null)
            properties["span"] = new
            {
                file = TargetRelativePath(targetPath, finding.Span.File),
                start = new { line = finding.Span.Start.Line, column = finding.Span.Start.Column },
                end = new { line = finding.Span.End.Line, column = finding.Span.End.Column },
            };

        return new
        {
            ruleId = Camel(finding.Kind.ToString()),
            level = "warning",
            message = new { text = DescribeFinding(finding) },
            partialFingerprints = new Dictionary<string, string> { ["knipId/v1"] = finding.Id },
            locations = new[]
            {
                new
                {
                    physicalLocation = new
                    {
                        artifactLocation = new { uri = TargetRelativePath(targetPath, finding.FilePath) },
                        region = finding.Line > 0
                            ? (object)new { startLine = finding.Line, startColumn = finding.Column }
                            : null,
                    },
                },
            },
            properties,
        };
    }

    private static string DescribeFinding(Finding finding) => finding.Kind switch
    {
        FindingKind.UnusedProjectReference =>
            $"Project '{finding.Project}' references '{finding.ReferencedProject ?? finding.Symbol}' but uses no type from it.",
        FindingKind.UnusedPackageReference =>
            $"Project '{finding.Project}' references package '{finding.Symbol}' but uses no type from it.",
        _ => $"Unused {finding.SymbolKind} '{finding.Symbol}' is never referenced.",
    };

    private static string DescribeKind(FindingKind kind) => kind switch
    {
        FindingKind.UnusedProjectReference => "An unused project reference.",
        FindingKind.UnusedPackageReference => "An unused package reference.",
        FindingKind.OnlyUsedByTests => "Production code reachable only from tests.",
        _ => $"An unused {Camel(kind.ToString().Substring("Unused".Length))} declaration.",
    };

    internal static string TargetRelativePath(string targetPath, string filePath)
    {
        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath))
            ?? Directory.GetCurrentDirectory();
        var baseUri = new Uri(AppendDirectorySeparator(targetDirectory));
        var fileUri = new Uri(Path.GetFullPath(filePath));
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString()).Replace('\\', '/');
    }

    private static string AppendDirectorySeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static string Camel(string value) =>
        value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);
}
