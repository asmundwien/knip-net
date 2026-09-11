using Knip.Core.Analysis;

namespace Knip.Cli;

internal static class ExplanationTextWriter
{
    public static void Write(ExplanationResult result, string targetPath, TextWriter output)
    {
        output.WriteLine($"reliability: {(result.Reliability.Degraded ? "degraded" : "complete")}");
        output.WriteLine($"status: {Status(result.Status!.Value)}");
        output.WriteLine($"symbol: {result.Symbol}");
        if (result.FindingId is not null)
            output.WriteLine($"finding: {result.FindingId}");
        output.WriteLine($"location: {Location(result.Location, targetPath)}");

        switch (result.Status)
        {
            case ExplanationStatus.Alive:
                WritePath("root path", result.Path, targetPath, output);
                break;
            case ExplanationStatus.OnlyUsedByTests:
                output.WriteLine("production root path: none");
                WritePath("test root path", result.Path, targetPath, output);
                break;
            case ExplanationStatus.Unreachable:
                if (result.Referrers.Count == 0)
                    output.WriteLine("incoming references: none");
                else
                {
                    output.WriteLine("unreachable referrers:");
                    foreach (var referrer in result.Referrers)
                        output.WriteLine($"  {referrer.Symbol} at {Location(referrer.Location, targetPath)}");
                }
                if (result.RootCauseId is not null)
                    output.WriteLine($"root cause: {result.RootCauseId} {result.RootCauseSymbol}");
                break;
        }
    }

    private static void WritePath(
        string label, IReadOnlyList<ExplanationHop> path, string targetPath, TextWriter output)
    {
        output.WriteLine($"{label}:");
        foreach (var hop in path)
            output.WriteLine($"  {hop.Symbol} at {Location(hop.Location, targetPath)}");
    }

    private static string Location(ExplanationLocation? location, string targetPath) => location is null
        ? "unknown"
        : $"{Reporting.TargetRelativePath(targetPath, location.File)}:{location.Line}:{location.Column}";

    private static string Status(ExplanationStatus status) => status switch
    {
        ExplanationStatus.Alive => "alive",
        ExplanationStatus.Unreachable => "unreachable",
        ExplanationStatus.OnlyUsedByTests => "only-used-by-tests",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
