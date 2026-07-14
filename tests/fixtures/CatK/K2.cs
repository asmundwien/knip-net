using CatK;

namespace CatK.K2;

// K2 (G-feat, PRODUCTION mode): a production type + method reachable ONLY via test roots.
// Under DEFAULT mode this is ALIVE (like K1). Under the unbuilt WS7 production mode the test roots
// are demoted, so both the method and its containing type become dead-reachable-only-from-tests and
// should be reported as OnlyUsedByTests. Fixture is compiled today; the assertion is Skip-tagged.
public sealed class Service
{
    // Under production mode: OnlyUsedByTests (its sole caller is a test root).
    public void ProductionMethod() { }
}

public sealed class ServiceTests
{
    [Fact]
    public void Exercises_ProductionMethod()
    {
        new Service().ProductionMethod();
    }
}
