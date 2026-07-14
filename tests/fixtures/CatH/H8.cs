using System;

namespace CatH.H8;

// H8 (G-moat): a WebForms code-behind class (Default.aspx.cs) and its OnClick handler are
// referenced ONLY from the .aspx markup (<%@ Page Inherits="..." %>, OnClick="Button_Click").
// The markup is not compiled C#, so the walker sees no reference and flags the class/handler dead.
// CORRECT eventual behavior (WS5 WebForms plugin): code-behind reachable from markup should be ALIVE.
// Mitigation today: ignore.files ["**/*.aspx.cs", "**/*.ascx.cs"] (skips code-behind entirely).
public sealed class DefaultPage
{
    // ALIVE (future): the page type is instantiated by the runtime from the .aspx Inherits directive.
    // Its event handler is wired via OnClick="Button_Click" in markup — never named in C#.
    public void Button_Click(object sender, EventArgs e) { }
}

// DEAD SIBLING (honest): an ordinary class not backing any markup, never referenced -> flagged.
public sealed class OrphanPage
{
    public void Button_Click(object sender, EventArgs e) { }
}
