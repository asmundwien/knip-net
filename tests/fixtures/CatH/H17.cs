using System.Threading.Tasks;

namespace CatH.AspNetTelemetry;

// H17 (PROMOTED — WS5 aspnetcore plugin): Application Insights TELEMETRY. A type implementing
// ITelemetryProcessor has its Process(ITelemetry) dispatched by the telemetry pipeline — never named in
// source; likewise ITelemetryInitializer.Initialize. The type is alive (DI-registered by generic arg), but
// the entry method gains no incoming edge, so the ctor-assigned _next + private helper (FjernSensitivData)
// CASCADE to dead false positives. The aspnetcore plugin (opt-in) roots Process/Initialize + instance ctors,
// so all of that is ALIVE with the plugin ON. Conservative: an unrelated dead method STAYS flagged
// (over-rooting guard). (~40 findings across Hdir.Hint.Logging.ApplicationInsights on real solutions.)

// Local stand-ins for Application Insights types — no real framework reference (invariant #9).
public interface ITelemetry { }

// The pipeline dispatches Process; each processor holds the _next processor (ctor-assigned) and calls it.
public interface ITelemetryProcessor
{
    void Process(ITelemetry item);
}

// The pipeline dispatches Initialize on each registered initializer.
public interface ITelemetryInitializer
{
    void Initialize(ITelemetry telemetry);
}

// The composition root: registers the processor/initializer so the TYPES are alive (mirrors
// .AddApplicationInsightsTelemetryProcessor<T>() / AddSingleton<ITelemetryInitializer, T>()).
public sealed class TelemetryRegistration
{
    public void Configure()
    {
        _ = typeof(SensitivDataTelemetryProcessor);
        _ = typeof(BrukerTelemetryInitializer);
    }
}

// The processor. The TYPE is alive; WITHOUT the plugin its Process (pipeline-dispatched) + ctor + _next field
// + private helper are all flagged. WITH the plugin, Process + ctor are rooted → _next (assigned in ctor) and
// FjernSensitivData (called by Process) gain liveness via the walker's edges.
public sealed class SensitivDataTelemetryProcessor : ITelemetryProcessor
{
    // ALIVE (plugin ON): assigned in the ctor (the load-bearing field — the next processor in the chain).
    private readonly ITelemetryProcessor _next;

    // ALIVE (plugin ON): the pipeline news the processor up via this ctor, injecting the next processor.
    public SensitivDataTelemetryProcessor(ITelemetryProcessor next)
    {
        _next = next;
    }

    // ALIVE (plugin ON): the pipeline-dispatched entry point; calls the private helper then the next processor.
    public void Process(ITelemetry item)
    {
        FjernSensitivData(item);
        _next.Process(item);
    }

    // ALIVE (plugin ON): reached from Process — gains liveness once the entry method is rooted.
    private void FjernSensitivData(ITelemetry item) { _ = item; }

    // DEAD SIBLING / OVER-ROOTING DECOY (honest): a public method the processor NEVER calls and no source
    // names -> flagged today AND with the plugin ON. The H17 over-rooting guard asserts it stays flagged.
    public void NeverProcessed() { }
}

// The initializer. The TYPE is alive; WITHOUT the plugin its Initialize (pipeline-dispatched) + the helper it
// calls are flagged. WITH the plugin, Initialize is rooted → the helper gains liveness.
public sealed class BrukerTelemetryInitializer : ITelemetryInitializer
{
    // ALIVE (plugin ON): the pipeline-dispatched entry point; calls the private helper.
    public void Initialize(ITelemetry telemetry)
    {
        SettBrukerkontekst(telemetry);
    }

    // ALIVE (plugin ON): reached from Initialize — gains liveness once the entry method is rooted.
    private void SettBrukerkontekst(ITelemetry telemetry) { _ = telemetry; }
}
