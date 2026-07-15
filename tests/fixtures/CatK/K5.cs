using CatK;

namespace CatK.K5;

// K5 (PRODUCTION mode, TRANSITIVE): A is used only by B; B is used only by a [Fact] test. Under
// production mode the test root is demoted, so B is test-only AND A (reachable only through B) is
// TRANSITIVELY test-only -> BOTH flagged OnlyUsedByTests.
//
// The TYPE is kept alive by a production caller (Entry.Main -> KeepAlive) so the two findings land at
// MEMBER granularity (Chain.A, Chain.B) rather than collapsing to the whole type.
public sealed class Chain
{
    // test-only, TRANSITIVELY (only caller is B, itself only called by a test).
    public void A() { }

    // test-only, DIRECTLY (only caller is the [Fact] below).
    public void B() => A();

    // Keeps the Chain TYPE alive in every mode.
    public void KeepAlive() { }
}

public sealed class Entry
{
    public static void Main()
    {
        new Chain().KeepAlive();
    }
}

public sealed class ChainTests
{
    [Fact]
    public void Exercises_B()
    {
        new Chain().B();
    }
}
