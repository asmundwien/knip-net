using System;

namespace CatH.QualifiedCollisions;

public interface IRequestHandler
{
    void Handle();
}

public abstract class Profile { }

public abstract class ComponentBase
{
    protected virtual void OnInitialized() { }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ParameterAttribute : Attribute { }

public static class JsonSerializer
{
    public static string Serialize(object value) => value.ToString() ?? "";
}

public sealed class ApplicationBuilder
{
    public void UseMiddleware<TMiddleware>() { }
}

public sealed class Handler : IRequestHandler
{
    public void Handle() => HandleCore();
    private void HandleCore() { }
}

public sealed class MappingProfile : Profile
{
    public void ConfigureMap() => ConfigureMapCore();
    private void ConfigureMapCore() { }
}

public sealed class Component : ComponentBase
{
    [Parameter]
    public string Value { get; set; } = "";

    protected override void OnInitialized() => InitializeCore();
    private void InitializeCore() { }
}

public sealed class Dto
{
    public string Value { get; set; } = "";
}

public sealed class Middleware
{
    public void Invoke() => InvokeCore();
    private void InvokeCore() { }
}

public sealed class Startup
{
    public void Configure()
    {
        _ = typeof(Handler);
        _ = typeof(MappingProfile);
        _ = typeof(Component);
        _ = JsonSerializer.Serialize(new Dto());
        new ApplicationBuilder().UseMiddleware<Middleware>();
    }
}
