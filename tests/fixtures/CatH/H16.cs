using System.Threading.Tasks;

namespace CatH.BlazorLifecycle;

// H16 (PROMOTED — WS5 aspnetcore plugin): Blazor COMPONENT LIFECYCLE. A type deriving from ComponentBase
// has its lifecycle methods (OnInitialized, OnParametersSetAsync, …) invoked by the Blazor renderer by
// CONVENTION — never named in source. So OnInitialized + the private helper it calls (LastInnData) CASCADE
// to dead false positives. The aspnetcore plugin (opt-in) roots the ComponentBase lifecycle methods when
// present, so the entry method + helper are ALIVE with the plugin ON. Conservative: an unrelated dead method
// the component never calls STAYS flagged (over-rooting guard). ([Parameter] props are the separate
// blazorParameter plugin's job — H16 covers the lifecycle METHODS only.)

// Local stand-in for Blazor's ComponentBase — no real framework reference (invariant #9). The renderer
// invokes the virtual lifecycle methods by convention; the base declares them so overrides bind.
public abstract class ComponentBase
{
    protected virtual void OnInitialized() { }
    protected virtual Task OnParametersSetAsync() => Task.CompletedTask;
}

// The composition root: the component TYPE is alive (referenced where routing/markup would name it).
public sealed class ComponentRegistration
{
    public void Configure()
    {
        // Referencing the type keeps it alive; the lifecycle methods are still convention-invoked by the renderer.
        _ = typeof(MyPage);
    }
}

// The component. The TYPE is alive; WITHOUT the plugin its OnInitialized (renderer-invoked) and the private
// helper it calls are flagged. WITH the plugin, the lifecycle method is rooted → LastInnData (called by it)
// gains liveness via the walker's edge.
public sealed class MyPage : ComponentBase
{
    // ALIVE (plugin ON): the convention lifecycle entry point; calls the private helper below.
    protected override void OnInitialized()
    {
        LastInnData();
    }

    // ALIVE (plugin ON): reached from OnInitialized — gains liveness once the lifecycle method is rooted.
    private void LastInnData() { }

    // DEAD SIBLING / OVER-ROOTING DECOY (honest): a public method the component NEVER calls and no source
    // names -> flagged today AND with the plugin ON. A blanket plugin that rooted the component's whole world
    // would wrongly keep this alive; the H16 over-rooting guard asserts it stays flagged.
    public void NeverRendered() { }
}
