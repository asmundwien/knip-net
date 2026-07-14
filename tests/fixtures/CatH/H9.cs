using System;

namespace CatH.H9;

// H9 (G-moat): a type wired up ONLY from web.config/app.config (e.g. an <httpModules> entry or a
// <provider type="CatH.H9.CustomProvider, Asm" />) is instantiated by the runtime from the config
// string. No C# names it, so the walker flags it dead.
// CORRECT eventual behavior (WS5 config plugin): config-referenced types should be ALIVE.
// Mitigation today: ignore.symbols ["CatH.H9.CustomProvider"].

// ALIVE (future): named only inside web.config's provider "type" attribute.
public sealed class CustomProvider
{
    public void Initialize() { }
}

// DEAD SIBLING (honest): a provider-shaped type never named in config or code -> flagged.
public sealed class UnusedProvider
{
    public void Initialize() { }
}
