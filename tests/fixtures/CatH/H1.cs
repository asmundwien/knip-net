using System;

namespace CatH.H1;

// H1 (G-moat): a method invoked ONLY via reflection (GetMethod("X").Invoke) is invisible to the
// walker — the string "Handle" is not an IdentifierName edge — so the tool flags Handle() dead.
// CORRECT eventual behavior (WS5 reflection plugin): Handle() should be ALIVE.
// Mitigation today: ignore.symbols ["CatH.H1.Service.Handle()"] (or a namespace glob).
public sealed class Service
{
    // The type is kept alive by a configured root name so the outermost-dead rule doesn't suppress
    // members — that isolates the moat to the individual member.
    public void ConfigureServices()
    {
        var type = typeof(Service); // typeof keeps the TYPE alive, not the string-named member
        var method = type.GetType().GetMethod("Handle"); // "Handle" is a plain string literal
        method?.Invoke(this, Array.Empty<object>());
    }

    // ALIVE (future): reached only by the reflected GetMethod("Handle").Invoke above.
    public void Handle() { }

    // DEAD SIBLING / OVER-ROOTING DECOY (honest): identical shape, never named anywhere -> flagged
    // today AND with the reflection plugin ON. A blanket plugin that rooted every member named in any
    // GetMethod string, or every member of a reflected type, would wrongly keep this alive — the H1
    // ALIVE-with-plugin test (and the WS5 over-rooting guard) assert it stays flagged.
    public void NeverCalled() { }
}
