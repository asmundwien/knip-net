namespace CatK.K4;

// K4 (Contract, WORKAROUND): a Lib whose production method is called ONLY from a separate *Tests
// project. This is the genuine 2-project analogue of K1's test-only reachability, structured so the
// `ignore.projects` workaround can simulate today's absent "production mode":
//   - DEFAULT config: the Tests project is loaded, its [Fact] roots TestOnly -> ALIVE.
//   - config.Ignore.Projects = ["*Tests*"]: the Tests project is SKIPPED (never loaded), so TestOnly
//     loses its only caller -> FLAGGED. That flagged result is itself the mutation of the default
//     setup (anti-vacuous pairing: same code, one config flip flips the verdict).
public sealed class Widget
{
    // Sole caller lives in CatK.K4.Tests. Alive by default; flagged once the Tests project is ignored.
    public void TestOnly() { }

    // DEAD SIBLING: no caller in ANY project -> flagged in BOTH configs. Proves the Lib is analyzed
    // independently of the ignore-projects flip, and that public != auto-alive.
    public void NeverCalled() { }

    // Keeps the Widget TYPE alive in BOTH configs (a genuine production caller, Entry.Run below), so
    // the finding stays at MEMBER granularity: when TestOnly flips to dead we still see
    // "Widget.TestOnly()" rather than the whole type collapsing to one outermost finding. This isolates
    // the K4 signal to the single method whose liveness the ignore-projects flip actually changes.
    public void KeepAlive() { }
}

// A real production root (Main is a default entry-point symbol name) with a non-test caller of the
// Widget, so Widget never dies as a type regardless of the ignore.projects flip.
public sealed class Entry
{
    public static void Main()
    {
        new Widget().KeepAlive();
    }
}
