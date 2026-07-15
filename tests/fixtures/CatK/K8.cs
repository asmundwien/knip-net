using CatK;

namespace CatK.K8;

// K8 (FIX #4 + WS7 origin guard): a test class with an EXPLICIT ctor that exercises production code
// (the common "arrange in the ctor" xUnit shape). FIX #4 roots the test class's instance ctor — but it
// MUST inherit the ctor's TEST origin, NOT production. So in PRODUCTION mode the production method
// reached ONLY from the test ctor must still be OnlyUsedByTests: the ctor being rooted does not make it
// production-reachable. This pins that FIX #4 did not regress the K two-color model (K-category recall).
public sealed class Service
{
    // PRODUCTION mode: OnlyUsedByTests — its sole caller is the TEST class ctor (a test-origin root).
    public void UsedFromTestCtor() { }

    // DEAD SIBLING (anti-vacuous): no caller in ANY mode -> ordinary UnusedMethod.
    public void NeverCalled() { }

    // Keeps the Service TYPE alive in every mode (real production caller) so the finding lands at member
    // granularity rather than collapsing to the whole type.
    public void KeepAlive() { }
}

public sealed class Entry
{
    // Real production root (Main) keeping the Service TYPE alive.
    public static void Main()
    {
        new Service().KeepAlive();
    }
}

public sealed class ServiceTests
{
    private readonly Service _service;

    // The test class's EXPLICIT ctor — rooted by FIX #4 as a TEST-origin root. It exercises the
    // production method; in production mode that reference is a TEST path only.
    public ServiceTests()
    {
        _service = new Service();
        _service.UsedFromTestCtor();
    }

    [Fact]
    public void Placeholder() { }
}
