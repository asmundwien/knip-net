namespace CatI.I4;

// I4 (SKIPPED project): under ignore.projects "CatI.I4.Skipped" this project is not loaded at all,
// so SkippedDead is neither analyzed nor reported and its assembly is absent from the solution set.
// RED-FLIP evidence: with NO ignore config, SkippedDead IS reported (the with/without diff proves
// the skip is what removes it, not that the fixture never reports it). A rooted entry point keeps
// the TYPE alive so the reported symbol is the METHOD.
public sealed class SkippedSample
{
    public void ConfigureServices() => Used();
    public void Used() { }

    public void SkippedDead() { }
}
