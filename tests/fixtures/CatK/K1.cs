using CatK;

namespace CatK.K1;

// K1 (Contract, DEFAULT mode): a production method referenced ONLY from a [Fact] test.
// The [Fact] method is a root (invariant #4: an entry-point member roots itself and its type chain),
// so the production method it calls is REACHABLE and NOT flagged. This is the deliberate false
// negative that WS7 "production mode" will later surface as an OnlyUsedByTests finding.
public sealed class Sample
{
    // ALIVE: reached only from the [Fact] test below. Under default semantics test roots keep it live.
    // Anti-vacuous pairing: its liveness is meaningful only because the DEAD SIBLING right beside it
    // (NeverCalled) IS flagged — proving the analyzer looks at this type and public != auto-alive.
    public void UsedOnlyByTest() { }

    // DEAD SIBLING: identical shape, called by NOBODY (not even a test) -> flagged.
    public void NeverCalled() { }
}

public sealed class SampleTests
{
    // ROOT: the sole use site of Sample.UsedOnlyByTest lives inside this [Fact] test.
    [Fact]
    public void Exercises_UsedOnlyByTest()
    {
        new Sample().UsedOnlyByTest();
    }
}
