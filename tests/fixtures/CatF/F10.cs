using System;

namespace CatF.F10;

// F10: MSTest static lifecycle hooks ([ClassInitialize]/[AssemblyInitialize]) and [DataTestMethod] are
// rooted by the DEFAULT config. These run once per class / per assembly and are invoked by the runner,
// never by name. LOCAL attribute stand-ins (matched by name) keep this zero-NuGet/offline.
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
