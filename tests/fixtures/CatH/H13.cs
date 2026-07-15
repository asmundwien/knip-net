using System;
using System.Threading.Tasks;

namespace CatH.AspNetMiddleware;

// H13 (PROMOTED — WS5 aspnetcore plugin): ASP.NET Core CONVENTION MIDDLEWARE. app.UseMiddleware<T>()
// keeps the TYPE alive (a generic-arg edge) but the middleware's Invoke(HttpContext) is invoked by the
// framework REFLECTIVELY — never named in source — so the walker flags Invoke dead, and its ctor +
// fields (_next/_logger) + private helper (LeggTilRequestMetadata) CASCADE to dead. The aspnetcore
// plugin (opt-in) roots T's Invoke/InvokeAsync + ctors, so all of that is ALIVE with the plugin ON.
// Conservative: an unrelated dead method Invoke never calls STAYS flagged (over-rooting guard).

// Local stand-ins for ASP.NET Core's types — no real framework reference (invariant #9).
public sealed class HttpContext { }

public delegate Task RequestDelegate(HttpContext context);

public interface ILogger
{
    void Log(string message);
}

// Local stand-in for IApplicationBuilder — carries the UseMiddleware<T>() convention method.
public sealed class ApplicationBuilder
{
    // Generic convention: activates T per pipeline and calls its Invoke/InvokeAsync reflectively.
    public ApplicationBuilder UseMiddleware<TMiddleware>() => this;
}

// The composition root: registers the middleware. Kept alive by ConfigureServices being a rooted host,
// so the outermost-dead rule doesn't hide the members and the moat isolates to individual members.
public sealed class Startup
{
    public void Configure()
    {
        var app = new ApplicationBuilder();
        // UseMiddleware<AuditLoggingMiddleware>() — a generic-arg edge keeps the TYPE alive; the framework
        // reflectively calls Invoke — never named here.
        app.UseMiddleware<AuditLoggingMiddleware>();
    }
}

// The middleware. The TYPE is alive (named in UseMiddleware<T>); WITHOUT the plugin its Invoke, ctor,
// fields and private helper are all flagged (the framework's reflective call is invisible). WITH the
// plugin, Invoke + the ctor are rooted → _next/_logger (assigned in the ctor, read in Invoke) and the
// private helper LeggTilRequestMetadata (called by Invoke) gain liveness via the walker's edges.
public sealed class AuditLoggingMiddleware
{
    // ALIVE (plugin ON): assigned in the ctor, read in Invoke — reachable once the ctor/Invoke are rooted.
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;

    // ALIVE (plugin ON): the RequestDelegate factory news the middleware up via this ctor.
    public AuditLoggingMiddleware(RequestDelegate next, ILogger logger)
    {
        _next = next;
        _logger = logger;
    }

    // ALIVE (plugin ON): the reflective convention entry point; calls the private helper below.
    public Task Invoke(HttpContext context)
    {
        LeggTilRequestMetadata(context);
        _logger.Log("audit");
        return _next(context);
    }

    // ALIVE (plugin ON): reached from Invoke — gains liveness for free once Invoke is rooted.
    private void LeggTilRequestMetadata(HttpContext context) { _ = context; }

    // DEAD SIBLING / OVER-ROOTING DECOY (honest): a public method Invoke NEVER calls and no source names
    // -> flagged today AND with the plugin ON. A blanket plugin that rooted the middleware's whole world
    // would wrongly keep this alive; the H13 over-rooting guard asserts it stays flagged.
    public void NeverInvokedByPipeline() { }
}
