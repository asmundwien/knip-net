namespace CatG.G3;

// G3: a primary-constructor class (C# 12), when used, must produce NO spurious findings. The primary
// ctor and its captured parameters are compiler-generated plumbing. DEAD SIBLING: an unused method on
// the same type IS flagged.
public sealed class Service(int seed)
{
    // Root: uses the captured primary-ctor parameter.
    public int ConfigureServices() => seed + Helper();

    // ALIVE: called from the root.
    private int Helper() => seed * 2;

    // DEAD SIBLING: unused member -> flagged.
    private int Unused() => seed;
}

public sealed class Root
{
    public int Configure() => new Service(1).ConfigureServices();
}
