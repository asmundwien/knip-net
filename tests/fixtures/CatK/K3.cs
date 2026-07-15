using CatK;

namespace CatK.K3;

// K3 (PRODUCTION mode): the OnlyUsedByTests finding must name the referencing TEST symbols, so a human
// sees the remediation unit (delete the test(s) then the production code). Two distinct [Fact] tests
// reference the same production method; the finding must enumerate BOTH referrers.
//
// The TYPE is kept alive by a production caller (Entry.Main) so the finding lands on the Add METHOD
// (which carries the test referrers), not on the whole type under outermost-only.
public sealed class Calculator
{
    // PRODUCTION mode: OnlyUsedByTests, referrers = {AlphaTests.Adds, BetaTests.AlsoAdds}.
    public int Add(int a, int b) => a + b;

    // Keeps the Calculator TYPE alive in every mode.
    public int KeepAlive() => 0;
}

public sealed class Entry
{
    public static void Main()
    {
        _ = new Calculator().KeepAlive();
    }
}

public sealed class AlphaTests
{
    [Fact]
    public void Adds()
    {
        _ = new Calculator().Add(1, 2);
    }
}

public sealed class BetaTests
{
    [Fact]
    public void AlsoAdds()
    {
        _ = new Calculator().Add(3, 4);
    }
}
