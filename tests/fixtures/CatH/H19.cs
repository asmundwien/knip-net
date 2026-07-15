using System.Threading.Tasks;

namespace CatH.AspNetPolicyProvider;

// H19 (PROMOTED — WS5 aspnetcore plugin): ASP.NET Core AUTHORIZATION POLICY PROVIDER. A type implementing
// IAuthorizationPolicyProvider (commonly by deriving from DefaultAuthorizationPolicyProvider) has its
// GetPolicyAsync/GetDefaultPolicyAsync/GetFallbackPolicyAsync dispatched by the authorization middleware —
// never named in source. The type is alive (DI-registered as IAuthorizationPolicyProvider), but the entry
// methods gain no incoming edge, so the ctor-assigned field (_options) + private helper (LagEntraIdPolicy)
// CASCADE to dead false positives. The aspnetcore plugin (opt-in) roots the Get*PolicyAsync entry methods +
// instance ctors, so all of that is ALIVE with the plugin ON. Conservative: an unrelated dead method STAYS
// flagged (over-rooting guard). (Dogfound as HintAuthorizationPolicyProvider on real Hdir solutions.)

// Local stand-ins for ASP.NET Core authorization types — no real framework reference (invariant #9).
public sealed class AuthorizationPolicy { }

public sealed class AuthorizationOptions { }

// The abstraction the authorization middleware dispatches through.
public interface IAuthorizationPolicyProvider
{
    Task<AuthorizationPolicy> GetPolicyAsync(string policyName);
    Task<AuthorizationPolicy> GetDefaultPolicyAsync();
    Task<AuthorizationPolicy> GetFallbackPolicyAsync();
}

// The framework's default provider — the common base derived-from. Matched by simple NAME
// "DefaultAuthorizationPolicyProvider". Local stand-in.
public abstract class DefaultAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    public virtual Task<AuthorizationPolicy> GetPolicyAsync(string policyName) { _ = policyName; return Task.FromResult(new AuthorizationPolicy()); }
    public virtual Task<AuthorizationPolicy> GetDefaultPolicyAsync() => Task.FromResult(new AuthorizationPolicy());
    public virtual Task<AuthorizationPolicy> GetFallbackPolicyAsync() => Task.FromResult(new AuthorizationPolicy());
}

// The composition root: registers the provider so the TYPE is alive (mirrors
// AddSingleton<IAuthorizationPolicyProvider, T>()).
public sealed class PolicyProviderRegistration
{
    public void Configure()
    {
        _ = typeof(HintAuthorizationPolicyProvider);
    }
}

// The provider. The TYPE is alive; WITHOUT the plugin its GetPolicyAsync (middleware-dispatched) + ctor +
// _options field + private helper are all flagged. WITH the plugin, the Get*PolicyAsync entry methods + ctor
// are rooted → _options (assigned in ctor) and LagEntraIdPolicy (called by GetPolicyAsync) gain liveness.
public sealed class HintAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
{
    // ALIVE (plugin ON): assigned in the ctor, read by the helper the entry method calls.
    private readonly AuthorizationOptions _options;

    // ALIVE (plugin ON): the framework news the provider up via this ctor (DI-injected collaborators).
    public HintAuthorizationPolicyProvider(AuthorizationOptions options)
    {
        _options = options;
    }

    // ALIVE (plugin ON): the middleware-dispatched entry point; calls the private helper.
    public override Task<AuthorizationPolicy> GetPolicyAsync(string policyName)
    {
        _ = policyName;
        return Task.FromResult(LagEntraIdPolicy());
    }

    // ALIVE (plugin ON): reached from GetPolicyAsync — gains liveness once the entry method is rooted.
    private AuthorizationPolicy LagEntraIdPolicy() { _ = _options; return new AuthorizationPolicy(); }

    // DEAD SIBLING / OVER-ROOTING DECOY (honest): a public method the provider NEVER calls and no source names
    // -> flagged today AND with the plugin ON. The H19 over-rooting guard asserts it stays flagged.
    public void NeverConsulted() { }
}
