using System;

namespace CatF.F9;

// F9: the local MSTest-shaped attribute is an explicit configured alias. Rooting Setup keeps its helper
// and containing type alive without treating every TestInitializeAttribute as framework-owned.
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestInitializeAttribute : Attribute { }

public sealed class LifecycleTests
{
    // ALIVE (root by default): [TestInitialize] runs before each test; the framework invokes it, so
    // nothing in source references it by name.
    [TestInitialize]
    public void Setup() => SharedArrange();

    // ALIVE: reached from the rooted setup method.
    private void SharedArrange() { }

    // DEAD SIBLING: identical shape, no lifecycle attribute, no caller -> flagged. Proves the row is
    // non-vacuous (the attribute, not mere presence in a test class, is what roots Setup()).
    public void UnattributedSetup() { }
}
