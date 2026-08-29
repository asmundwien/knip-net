using System.Threading.Tasks;

namespace CatH.AspNetFilter;

// H14 (PROMOTED — WS5 aspnetcore plugin): ASP.NET Core MVC FILTER. A type implementing IAsyncActionFilter
// has its OnActionExecutingAsync dispatched by the framework filter pipeline REFLECTIVELY — the interface
// member is never referenced in source. The concrete impl is suppressed by the interface-implementation
// rule, but it gains no incoming edge, so the private helper it calls (LeggTilTjenestenavn, literally
// invoked at the top of OnActionExecutingAsync) CASCADES to a dead false positive. The aspnetcore plugin
// (opt-in) roots the type's implementations of the filter interface methods, so OnActionExecutingAsync is
// reachable and LeggTilTjenestenavn is ALIVE with the plugin ON. Conservative: an unrelated dead method the
// filter never calls STAYS flagged (over-rooting guard).

// Local stand-ins for ASP.NET Core MVC's filter contracts — no real framework reference (invariant #9).
public sealed class ActionExecutingContext { }
public sealed class ActionExecutionDelegate { }

public interface IAsyncActionFilter
{
    Task OnActionExecutingAsync(ActionExecutingContext context, ActionExecutionDelegate next);
}

// The composition root: registers the filter so the TYPE is alive (mirrors AddMvc(o => o.Filters.Add<T>())).
// The registration references the type but the framework calls the interface method reflectively.
public sealed class FilterRegistration
{
    public void Configure()
    {
        // Referencing the type keeps it alive; the interface method is still framework-dispatched.
        _ = typeof(AuditFilter);
    }
}

// The filter. The TYPE is alive (constructed in registration); WITHOUT the plugin its OnActionExecutingAsync
// (framework-dispatched) and the private helper it calls are flagged. WITH the plugin, the interface-method
// implementation is rooted → LeggTilTjenestenavn (called by it) gains liveness via the walker's edge.
public sealed class AuditFilter : IAsyncActionFilter
{
    private readonly string _state;

    public AuditFilter()
    {
        _state = BuildState();
    }

    private static string BuildState() => "ready";

    // ALIVE (plugin ON): the reflectively-dispatched filter entry point; calls the private helper below.
    public Task OnActionExecutingAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        LeggTilTjenestenavn(context);
        return Task.CompletedTask;
    }

    // ALIVE (plugin ON): reached from OnActionExecutingAsync — gains liveness once the entry method is rooted.
    private void LeggTilTjenestenavn(ActionExecutingContext context) { _ = context; }

    // DEAD SIBLING / OVER-ROOTING DECOY (honest): a public method the filter NEVER calls and no source
    // names -> flagged today AND with the plugin ON. A blanket plugin that rooted the filter's whole world
    // would wrongly keep this alive; the H14 over-rooting guard asserts it stays flagged.
    public void NeverDispatched() => System.Console.WriteLine(_state);
}
