using System;
using System.Threading.Tasks;

namespace CatH.AspNetFrameworkActivation;

public sealed class HttpContext { }

public interface IMiddleware
{
    Task InvokeAsync(HttpContext context, Func<Task> next);
}

public interface IStartupFilter
{
    Action Configure(Action next);
}

public sealed class FactoryMiddleware : IMiddleware
{
    private readonly string _state;

    public FactoryMiddleware()
    {
        _state = BuildState();
    }

    private static string BuildState() => "ready";

    public Task InvokeAsync(HttpContext context, Func<Task> next) => next();

    public void NeverInvoked() => Console.WriteLine(_state);
}

public sealed class RequestPipelineFilter : IStartupFilter
{
    private readonly string _state;

    public RequestPipelineFilter()
    {
        _state = BuildState();
    }

    private static string BuildState() => "ready";

    public Action Configure(Action next) => next;

    public void NeverConfigured() => Console.WriteLine(_state);
}

public sealed class Startup
{
    public void ConfigureServices()
    {
        _ = typeof(FactoryMiddleware);
        _ = typeof(RequestPipelineFilter);
    }
}
