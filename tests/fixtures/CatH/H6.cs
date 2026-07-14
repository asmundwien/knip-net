using System;

namespace CatH.H6;

// H6 (G-moat): a Blazor [Parameter] property is set by the framework from Razor markup
// (<MyComponent Title="..." />) — never assigned in C# source — so the walker flags it dead.
// CORRECT eventual behavior (config today, plugin longer-term): [Parameter] props should be ALIVE.
// Mitigation today: entryPoints.attributes ["Parameter"] (marks the property a root).

// Local stand-in for Blazor's ParameterAttribute. No real framework reference.
[AttributeUsage(AttributeTargets.Property)]
public sealed class ParameterAttribute : Attribute { }

public sealed class MyComponent
{
    // Rooted host: kept alive so the outermost-dead rule doesn't hide the member.
    public void ConfigureServices() { }

    // ALIVE (future): set from markup only; no C# assignment/read exists.
    [Parameter]
    public string Title { get; set; } = "";

    // DEAD SIBLING (honest): an ordinary property with no [Parameter] and no source use -> flagged.
    public string Unbound { get; set; } = "";
}
