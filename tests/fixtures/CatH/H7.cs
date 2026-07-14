using System;

namespace CatH.H7;

// H7 (G-moat): a WPF/MAUI view-model property bound ONLY from XAML markup ({Binding Greeting})
// is set/read by the binding engine via reflection — never touched in C# — so the walker flags it
// dead. Likewise a command method invoked only from XAML.
// CORRECT eventual behavior (WS5 XAML plugin): XAML-bound members should be ALIVE.
// Mitigation today: ignore.symbols on the view-model, e.g. ignore.namespaces ["CatH.H7"].
public sealed class MainViewModel
{
    // Rooted host so the outermost-dead rule doesn't hide the member.
    public void ConfigureServices() { }

    // ALIVE (future): bound from XAML {Binding Greeting}; no C# reader/writer exists.
    public string Greeting { get; set; } = "hello";

    // ALIVE (future): invoked from XAML Command="{Binding SaveCommand}" style wiring.
    public void Save() { }

    // DEAD SIBLING (honest): neither bound in XAML nor used in code -> flagged.
    public string Unbound { get; set; } = "";
}
