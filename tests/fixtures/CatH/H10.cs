using System;

namespace CatH.H10;

// H10 (G-moat): a method invoked via dynamic dispatch ((dynamic)x).Compute() is bound at RUNTIME.
// GetSymbolInfo on the dynamic invocation yields no concrete symbol, so the walker records no edge
// and flags Compute() dead. This is fundamentally UNDECIDABLE at compile time (documented FP);
// even a WS5 plugin cannot generally resolve dynamic targets.
// "Correct" behavior we assert (a plugin/heuristic keeping dynamically-dispatched names alive):
// Compute() should be ALIVE. Mitigation today: ignore.symbols ["CatH.H10.Widget.Compute()"].
public sealed class Widget
{
    // ALIVE (future): invoked only through the dynamic cast below.
    public void Compute() { }

    // DEAD SIBLING (honest): never invoked, dynamically or otherwise -> flagged.
    public void NeverInvoked() { }
}

public sealed class Caller
{
    public void ConfigureServices()
    {
        object x = new Widget(); // Widget TYPE alive (object creation); the METHOD is not
        ((dynamic)x).Compute();  // dynamic dispatch -> no static edge to Compute()
    }
}
