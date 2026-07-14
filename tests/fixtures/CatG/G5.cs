namespace CatG.G5;

// G5: a nested private type used only by its outer type is ALIVE (reached via the outer type's use).
// DEAD SIBLING: a nested type with no user is flagged.
public sealed class Outer
{
    // Root: instantiates and uses the nested type.
    public int ConfigureServices() => new UsedNested().Value;

    // ALIVE: used by the outer type.
    private sealed class UsedNested
    {
        public int Value => 42;
    }

    // DEAD SIBLING: nested type never referenced -> flagged.
    private sealed class UnusedNested
    {
        public int Value => 0;
    }
}
