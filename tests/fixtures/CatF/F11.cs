using System;

namespace CatF.F11;

// F11: NUnit one-time lifecycle hooks ([OneTimeSetUp]/[OneTimeTearDown]) are rooted by the DEFAULT
// config. They run once per test fixture and are invoked by the runner, never by name. LOCAL attribute
// stand-ins (matched by name) keep this zero-NuGet/offline.
[AttributeUsage(AttributeTargets.Method)]
public sealed class OneTimeSetUpAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class OneTimeTearDownAttribute : Attribute { }

public sealed class Fixture
{
    // ALIVE (root by default): [OneTimeSetUp] runs once before the fixture's tests.
    [OneTimeSetUp]
    public void BeforeAll() { }

    // ALIVE (root by default): [OneTimeTearDown] runs once after the fixture's tests.
    [OneTimeTearDown]
    public void AfterAll() { }

    // DEAD SIBLING: same shape, no lifecycle attribute, no caller -> flagged.
    public void NotAHook() { }
}
