using System.Threading.Tasks;

namespace CatH.AspNetAuthHandler;

// H15 (PROMOTED — WS5 aspnetcore plugin): ASP.NET Core AUTHORIZATION HANDLER. A type deriving from
// AuthorizationHandler<TRequirement> has its HandleRequirementAsync dispatched by policy evaluation
// REFLECTIVELY — never named in source. So the entry method + ctor + fields (_logger,
// _authenticationStateProvider) + private helper (SjekkTilgang) CASCADE to dead false positives. The
// aspnetcore plugin (opt-in) roots the handler's HandleRequirementAsync/HandleAsync + instance ctors, so
// all of that is ALIVE with the plugin ON. Conservative: an unrelated dead method the handler never calls
// STAYS flagged (over-rooting guard).

// Local stand-ins for ASP.NET Core's authorization types — no real framework reference (invariant #9).
public sealed class AuthorizationHandlerContext { }

public interface ILogger
{
    void Log(string message);
}

public sealed class AuthenticationStateProvider { }

public interface IAuthorizationRequirement { }

// The abstract base the framework's policy engine dispatches through. The fixture config explicitly aliases
// it to Microsoft.AspNetCore.Authorization.AuthorizationHandler.
public abstract class AuthorizationHandler<TRequirement>
    where TRequirement : IAuthorizationRequirement
{
    protected abstract Task HandleRequirementAsync(AuthorizationHandlerContext context, TRequirement requirement);
}

// A requirement marker (set from a policy) — left to DI/other plugins, not this handler-entry rule.
public sealed class ADGroupsRequirement : IAuthorizationRequirement { }

// The composition root: registers the handler so the TYPE is alive (mirrors AddSingleton<IAuthorizationHandler, T>()).
public sealed class AuthRegistration
{
    public void Configure()
    {
        // Referencing the type keeps it alive; the entry method is still framework-dispatched.
        _ = typeof(ADGroupsHandler);
    }
}

// The handler. The TYPE is alive (referenced in registration); WITHOUT the plugin its HandleRequirementAsync
// (framework-dispatched) + ctor + fields + private helper are all flagged. WITH the plugin, the entry method
// + ctor are rooted → _logger/_authenticationStateProvider (assigned in ctor) and the private helper
// SjekkTilgang (called by HandleRequirementAsync) gain liveness via the walker's edges.
public sealed class ADGroupsHandler : AuthorizationHandler<ADGroupsRequirement>
{
    // ALIVE (plugin ON): assigned in the ctor, read in the entry method — reachable once ctor/entry are rooted.
    private readonly ILogger _logger;
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    // ALIVE (plugin ON): the framework news the handler up via this ctor (DI-injected collaborators).
    public ADGroupsHandler(ILogger logger, AuthenticationStateProvider authenticationStateProvider)
    {
        _logger = logger;
        _authenticationStateProvider = authenticationStateProvider;
    }

    // ALIVE (plugin ON): the reflectively-dispatched policy-evaluation entry point; calls the private helper.
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ADGroupsRequirement requirement)
    {
        SjekkTilgang(context);
        _logger.Log("authz");
        _ = _authenticationStateProvider;
        return Task.CompletedTask;
    }

    // ALIVE (plugin ON): reached from HandleRequirementAsync — gains liveness once the entry method is rooted.
    private void SjekkTilgang(AuthorizationHandlerContext context) { _ = context; }

    // DEAD SIBLING / OVER-ROOTING DECOY (honest): a public method the handler NEVER calls and no source names
    // -> flagged today AND with the plugin ON. A blanket plugin that rooted the handler's whole world would
    // wrongly keep this alive; the H15 over-rooting guard asserts it stays flagged.
    public void NeverEvaluated() { }
}
