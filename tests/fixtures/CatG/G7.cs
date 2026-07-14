namespace CatG.G7;

// G7: constructors, static constructors and finalizers are NEVER reported (invariant #7), even when
// they have no explicit caller. The type is kept alive by the root. DEAD SIBLING: an ordinary unused
// method on the same type IS flagged, proving the type isn't wholesale suppressed.
public sealed class Sample
{
    // Instance constructor: never reported.
    public Sample() { }

    // Static constructor: never reported.
    static Sample() { }

    // Finalizer: never reported.
    ~Sample() { }

    // Root: keeps the type alive.
    public void ConfigureServices() { }

    // DEAD SIBLING: ordinary unused method -> flagged.
    public void UnusedMethod() { }
}
