using System;

namespace CatF.F10;

// F10: local MSTest-shaped lifecycle attributes are explicit configured aliases. The methods are invoked
// by the represented runner and never named in source.
[AttributeUsage(AttributeTargets.Method)]
public sealed class ClassInitializeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class AssemblyInitializeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class DataTestMethodAttribute : Attribute { }

public sealed class StaticHooks
{
    // ALIVE (root by default): static [ClassInitialize] hook.
    [ClassInitialize]
    public static void ClassSetup() { }

    // ALIVE (root by default): static [AssemblyInitialize] hook.
    [AssemblyInitialize]
    public static void AssemblySetup() { }

    // ALIVE (root by default): [DataTestMethod] is a parameterized test method.
    [DataTestMethod]
    public void DataDriven() { }

    // DEAD SIBLING: same static shape, no lifecycle attribute, no caller -> flagged.
    public static void UnattributedStaticSetup() { }
}
