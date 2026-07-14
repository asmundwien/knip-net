using CatK;

namespace CatK.K5;

// K5 (G-feat, PRODUCTION mode, transitive): A is used only by B; B is used only by tests.
// Under production mode the test root is demoted, so B is test-only AND A (reachable only through B)
// is transitively test-only -> BOTH flagged OnlyUsedByTests. Compiled today; Skip-tagged (WS7).
public sealed class Chain
{
    // test-only, transitively (only caller is B, which is only called by a test).
    public void A() { }

    // test-only, directly (only caller is the [Fact] below).
    public void B() => A();
}

public sealed class ChainTests
{
    [Fact]
    public void Exercises_B()
    {
        new Chain().B();
    }
}
