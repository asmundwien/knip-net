using System;

namespace CatF.F11;

// F11: local NUnit-shaped lifecycle attributes are explicit configured aliases. They keep this fixture
// offline without broadening the built-in framework identities.
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
