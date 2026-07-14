namespace Knip.Core.Model;

public sealed class AnalysisResult
{
    public List<Finding> Findings { get; } = [];

    /// <summary>Non-fatal problems loading the solution (missing SDK workloads, unresolved refs, ...).</summary>
    public List<string> LoadDiagnostics { get; } = [];

    public int ProjectsAnalyzed { get; set; }
    public int SymbolsAnalyzed { get; set; }
    public int RootCount { get; set; }
    public TimeSpan Elapsed { get; set; }
}
