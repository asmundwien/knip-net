using System;

namespace CatF.F9;

// F9: MSTest [TestInitialize] setup method is rooted by the DEFAULT config (the shipped defaults now
// include the MSTest lifecycle hooks). Rooting the setup method keeps a helper it calls alive and its
// containing type alive (EvaluateRoots walks the ContainingType chain). Attributes match by NAME
// with/without the "Attribute" suffix, so a LOCAL TestInitializeAttribute suffices — no MSTest package.
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
