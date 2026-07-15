using System.Threading.Tasks;

namespace CatH.AspNetHealthCheck;

// H18 (PROMOTED — WS5 aspnetcore plugin): ASP.NET Core HEALTH CHECK. A type implementing IHealthCheck has its
// CheckHealthAsync dispatched by the health-check middleware — never named in source. The type is alive
// (DI-registered via AddHealthChecks().AddCheck<T>()), but the entry method gains no incoming edge, so the
// ctor-assigned field (_configuration) + private helper (LesTerskel) CASCADE to dead false positives. The
// aspnetcore plugin (opt-in) roots CheckHealthAsync + instance ctors, so all of that is ALIVE with the plugin
// ON. Conservative: an unrelated dead method STAYS flagged (over-rooting guard).

// Local stand-ins for ASP.NET Core health-check types — no real framework reference (invariant #9).
public sealed class HealthCheckContext { }

public sealed class HealthCheckResult { }

public sealed class Configuration
{
    public int Read(string key) { _ = key; return 0; }
}

// The abstraction the health-check middleware dispatches through: CheckHealthAsync is called per probe.
public interface IHealthCheck
{
    Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context);
}

// The composition root: registers the check so the TYPE is alive (mirrors AddHealthChecks().AddCheck<T>()).
public sealed class HealthCheckRegistration
{
    public void Configure()
    {
        _ = typeof(ConfigurationHealthCheck);
    }
}

// The health check. The TYPE is alive; WITHOUT the plugin its CheckHealthAsync (middleware-dispatched) + ctor
// + _configuration field + private helper are all flagged. WITH the plugin, CheckHealthAsync + ctor are
// rooted → _configuration (assigned in ctor) and LesTerskel (called by CheckHealthAsync) gain liveness.
public sealed class ConfigurationHealthCheck : IHealthCheck
{
    // ALIVE (plugin ON): assigned in the ctor, read by the helper the entry method calls.
    private readonly Configuration _configuration;

    // ALIVE (plugin ON): the framework news the check up via this ctor (DI-injected collaborators).
    public ConfigurationHealthCheck(Configuration configuration)
    {
        _configuration = configuration;
    }

    // ALIVE (plugin ON): the middleware-dispatched entry point; calls the private helper.
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context)
    {
        _ = context;
        LesTerskel();
        return Task.FromResult(new HealthCheckResult());
    }

    // ALIVE (plugin ON): reached from CheckHealthAsync — gains liveness once the entry method is rooted.
    private int LesTerskel() => _configuration.Read("terskel");

    // DEAD SIBLING / OVER-ROOTING DECOY (honest): a public method the check NEVER calls and no source names
    // -> flagged today AND with the plugin ON. The H18 over-rooting guard asserts it stays flagged.
    public void NeverProbed() { }
}
