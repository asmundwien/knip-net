using System;

namespace CatH.H6;

// H6 (PROMOTED — WS5 blazorParameter plugin): Blazor component members set from .razor markup or the DI
// container are never assigned in C# source, so the walker flags them dead. The blazorParameter plugin
// (opt-in) roots members carrying [Parameter] / [CascadingParameter] / [Inject] (matched by attribute
// NAME, offline) — so they are ALIVE with the plugin ON. Conservative: only attribute-bearing members are
// rooted; a plain sibling property and an unrelated dead type stay flagged (over-rooting guard).

// Local stand-ins for Blazor's component attributes. No real framework reference (invariant #9).
[AttributeUsage(AttributeTargets.Property)]
public sealed class ParameterAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class CascadingParameterAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class InjectAttribute : Attribute { }

public sealed class MyComponent
{
    // Rooted host: kept alive so the outermost-dead rule doesn't hide the members.
    public void ConfigureServices() { }

    // ALIVE (plugin ON): set from markup only; no C# assignment/read exists.
    [Parameter]
    public string Title { get; set; } = "";

    // ALIVE (plugin ON): supplied by a <CascadingValue> ancestor from markup.
    [CascadingParameter]
    public string Theme { get; set; } = "";

    // ALIVE (plugin ON): assigned by the DI container, never in source.
    [Inject]
    public string Clock { get; set; } = "";

    // DEAD SIBLING / OVER-ROOTING DECOY (honest): an ordinary property with no rooting attribute and no
    // source use -> flagged today AND with the plugin ON (the plugin roots only attribute-bearing members).
    public string Unbound { get; set; } = "";
}

// DEAD DECOY (honest): an unrelated type, never referenced, no rooting attribute -> flagged with the
// plugin ON. A blanket "root the component's world" plugin would wrongly keep this alive.
public sealed class UnrelatedType
{
    public void NeverCalled() { }
}
