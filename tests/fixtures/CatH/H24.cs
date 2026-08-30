namespace CatH.QualifiedFrameworkShapes;

public sealed class FrameworkEndpoint
{
    [Microsoft.AspNetCore.Mvc.Route("framework")]
    public void Routed() => RoutedCore();

    private void RoutedCore() { }

    public void NeverCalled() { }
}

public sealed class FrameworkComponent : Microsoft.AspNetCore.Components.ComponentBase
{
    [Microsoft.AspNetCore.Components.Parameter]
    public string Value { get; set; } = "";

    protected override void OnInitialized() => InitializeCore();

    private void InitializeCore() { }

    private void NeverRendered() { }
}

public sealed class Startup
{
    public void Configure()
    {
        _ = typeof(FrameworkEndpoint);
        _ = typeof(FrameworkComponent);
    }
}
