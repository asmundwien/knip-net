namespace CatI.I8;

// I8: a fully-resolved, clean scenario. Every referenced type is declared/imported, so Roslyn
// yields no TypeKind.Error types and the unresolved-type warning is ABSENT from LoadDiagnostics.
// (Asserted against the Main fixture solution, which is entirely clean-compiling.)
public sealed class Resolved
{
    // All types here resolve (int, the local Helper type). Dead sibling for good measure.
    public Helper Make() => new Helper();

    public int Compute(int x) => x + 1;
}

public sealed class Helper
{
}
