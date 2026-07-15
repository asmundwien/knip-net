using System;

namespace CatF.F12;

// F12 (FIX #4a): the framework CONSTRUCTS a test class to invoke its [Fact] — so the instance ctor is
// USED. A field assigned ONLY in the ctor and a helper called ONLY from the ctor (the classic xUnit
// test-setup shape: `_loggerMock = ...; SetupCommonMocks();`) must therefore stay ALIVE. Before FIX #4
// only the [Fact] method + type were rooted, not the ctor, so these cascaded to HIGH-confidence dead.
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute { }

public sealed class SampleTests
{
    // ALIVE: assigned ONLY in the ctor (never read by the [Fact]) — kept alive because the ctor is a
    // root (the framework news the class to run the [Fact]).
    private readonly object _loggerMock;

    // DEAD SIBLING (anti-vacuous): a field NEVER assigned or read anywhere -> flagged. Proves the type
    // is not wholesale-rooted and that only ctor-reachable members survive.
    private readonly object _neverUsed;

    public SampleTests()
    {
        _loggerMock = new object();
        SetupCommonMocks();
    }

    // ALIVE: called ONLY from the ctor -> reachable because the ctor is rooted.
    private void SetupCommonMocks() { }

    [Fact]
    public void Exercises_Something() { }
}
