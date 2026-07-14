using System;

namespace CatH.H2;

// H2 (G-moat): a type named ONLY inside a string passed to Type.GetType("CatH.H2.Plugin") is
// invisible to the walker (the string is not an IdentifierName edge), so the tool flags Plugin dead.
// CORRECT eventual behavior (WS5 reflection plugin): Plugin should be ALIVE.
// Mitigation today: ignore.symbols ["CatH.H2.Plugin"].
public sealed class Loader
{
    public void ConfigureServices()
    {
        // Only reference to Plugin is this string literal — no IdentifierName node names the type.
        var t = Type.GetType("CatH.H2.Plugin");
        _ = t;
    }
}

// ALIVE (future): referenced only via the "CatH.H2.Plugin" string above.
public sealed class Plugin
{
    public void Run() { }
}

// DEAD SIBLING (honest): never named in any string or code -> flagged today AND in future.
public sealed class UnusedPlugin
{
    public void Run() { }
}
