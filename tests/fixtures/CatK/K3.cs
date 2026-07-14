using CatK;

namespace CatK.K3;

// K3 (G-feat, PRODUCTION mode): the OnlyUsedByTests finding must name the referencing TEST symbols,
// so a human can see the remediation unit (delete the test(s) then the production code, or promote a
// real caller). Two distinct tests reference the same production method; the future finding should
// enumerate both referrers. Compiled today; assertion Skip-tagged (WS7).
public sealed class Calculator
{
    // Under production mode: OnlyUsedByTests, referrers = {AlphaTests.Adds, BetaTests.AlsoAdds}.
    public int Add(int a, int b) => a + b;
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
