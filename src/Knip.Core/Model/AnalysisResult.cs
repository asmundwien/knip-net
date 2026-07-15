namespace Knip.Core.Model;

/// <summary>Severity of a structured load diagnostic (WS8 reliability block).</summary>
public enum LoadSeverity
{
    Warning,
    Error,
}

/// <summary>A structured load diagnostic: the existing LoadDiagnostics channel, machine-readable.</summary>
public sealed record LoadDiagnostic(LoadSeverity Severity, string Code, string Message);

/// <summary>A project that failed to load, attributed per-project (needed later for C1 per-project demotion).</summary>
public sealed record ProjectLoadFailure(string Project, string Message);

/// <summary>
/// The run's trustworthiness signal (WS8 §1.1). An agent reads <see cref="Degraded"/> to gate autonomous
/// action; the detail fields explain why. Populated by the analyzer; <see cref="Degraded"/> is derived.
/// </summary>
public sealed class Reliability
{
    /// <summary>Number of projects successfully analyzed.</summary>
    public int ProjectsLoaded { get; set; }

    /// <summary>Projects that failed to load, per-project (for C1 attribution).</summary>
    public List<ProjectLoadFailure> ProjectsFailed { get; } = [];

    /// <summary>Count of references to unresolved (error) types — the invariant-#6 restore signal.</summary>
    public int UnresolvedTypeReferences { get; set; }

    /// <summary>Per-project restore/load failure detail strings.</summary>
    public List<string> RestoreFailures { get; } = [];

    /// <summary>The load-diagnostics channel, structured (severity/code/message).</summary>
    public List<LoadDiagnostic> LoadDiagnostics { get; } = [];

    /// <summary>
    /// OR of: any <see cref="ProjectsFailed"/>, <see cref="UnresolvedTypeReferences"/> &gt; 0, any
    /// <see cref="RestoreFailures"/>, or any error-severity <see cref="LoadDiagnostics"/> entry.
    /// </summary>
    public bool Degraded =>
        ProjectsFailed.Count > 0
        || UnresolvedTypeReferences > 0
        || RestoreFailures.Count > 0
        || LoadDiagnostics.Any(d => d.Severity == LoadSeverity.Error);
}

public sealed class AnalysisResult
{
    public List<Finding> Findings { get; } = [];

    /// <summary>Non-fatal problems loading the solution (missing SDK workloads, unresolved refs, ...).</summary>
    public List<string> LoadDiagnostics { get; } = [];

    /// <summary>Trust signal for the run (WS8 §1.1).</summary>
    public Reliability Reliability { get; } = new();

    public int ProjectsAnalyzed { get; set; }
    public int SymbolsAnalyzed { get; set; }
    public int RootCount { get; set; }
    public TimeSpan Elapsed { get; set; }
}
