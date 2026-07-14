namespace CatD.D3;

// D3: an EXPLICIT interface implementation is NEVER reported (invariant #7), even when the interface
// member itself is never invoked. Here IWidget.Render is not called anywhere, yet the explicit impl
// Widget.IWidget.Render must not appear in the finding set. The interface IWidget IS referenced
// (Widget : IWidget, and a Widget is created), so IWidget is alive; only the uncalled ordinary
// method UnusedHelper is flagged.
//
// Mechanism: this row's assertion is a NEGATIVE ("explicit impl never reported"); its dead sibling is
// UnusedHelper, whose presence in the finding set proves the run is not vacuously empty.
public interface IWidget
{
    void Render();
}

public sealed class Widget : IWidget
{
    // Explicit interface implementation: NEVER reported even though never dispatched.
    void IWidget.Render() { }

    // DEAD SIBLING (mutation check): ordinary uncalled method -> flagged.
    public void UnusedHelper() { }
}

public sealed class Runner
{
    public void ConfigureServices()
    {
        // Reference IWidget + create Widget so their type nodes are alive; do NOT call Render.
        IWidget w = new Widget();
        _ = w;
    }
}
