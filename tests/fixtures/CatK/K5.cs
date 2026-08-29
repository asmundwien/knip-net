using CatK;

namespace CatK.K5;

// K5 (PRODUCTION mode, TRANSITIVE): internal A is used only by public B; B is used only by a [Fact]
// test. Both are test-only, but B is the low-confidence public boundary. The deletion chain must point
// from A to B so a consumer does not treat A as an independently actionable medium-confidence unit.
//
// The TYPE is kept alive by a production caller (Entry.Main -> KeepAlive) so the two findings land at
// MEMBER granularity (Chain.A, Chain.B) rather than collapsing to the whole type.
public sealed class Chain
{
    // Transitively test-only and internal: medium confidence, but covered by deleting B.
    internal void A() { }

    // Directly test-only and public: low-confidence public boundary with the test referrer.
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
