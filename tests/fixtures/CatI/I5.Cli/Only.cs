namespace CatI.I5;

// I5 (CLI override target): exactly ONE dead symbol (the method OnlyDead). With default config the
// CLI reports it and exits 1. With --config pointing at override.knip.json (which ignores this
// symbol) the CLI reports nothing and exits 0 — proving --config overrides discovery. A rooted
// entry point keeps the TYPE alive so the single dead symbol is the METHOD the override glob targets.
public sealed class OnlySample
{
    public void ConfigureServices() => Used();
    public void Used() { }

    public void OnlyDead() { }
}
