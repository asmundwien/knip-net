using CatK;

namespace CatK.K2;

// K2 (PRODUCTION mode): a production method reachable ONLY via a test root is OnlyUsedByTests.
// Under DEFAULT mode this is ALIVE (like K1). Under production mode the test root is demoted, so the
// method becomes dead-reachable-only-from-tests and is reported as OnlyUsedByTests.
//
// The TYPE is kept alive by a genuine production caller (Entry.Main) so the finding lands at MEMBER
// granularity (Service.ProductionMethod), not collapsing to the whole type under outermost-only. This
// mirrors K4's KeepAlive pattern and isolates the K2 signal to the one method the tests keep alive.
public sealed class Service
{
    // PRODUCTION mode: OnlyUsedByTests (its sole caller is a [Fact] test root).
    public void ProductionMethod() { }

    // DEAD SIBLING (anti-vacuous): no caller in ANY mode -> ordinary UnusedMethod finding. Proves the
    // type is analyzed and that OnlyUsedByTests is distinct from plain-dead.
    public void NeverCalled() { }

    // Keeps the Service TYPE alive in every mode (a real production caller below), so ProductionMethod
    // reports as a member rather than the whole type collapsing to one outermost finding.
    public void KeepAlive() { }
}

public sealed class Entry
{
    // A real production root (Main is a default entry-point symbol name) exercising Service.
    public static void Main()
    {
        new Service().KeepAlive();
    }
}

public sealed class ServiceTests
{
    [Fact]
    public void Exercises_ProductionMethod()
    {
        new Service().ProductionMethod();
    }
}
