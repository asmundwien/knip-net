namespace CatG.G10;

// G10: a const field is folded at compile time, but Roslyn still resolves an IdentifierName at the use
// site, so a referenced const stays ALIVE. DEAD SIBLING: an unused const is flagged.
public sealed class Sample
{
    // ALIVE: referenced below.
    public const int Used = 1;

    // DEAD SIBLING: const never referenced -> flagged.
    public const int Unused = 2;

    // Root: references the used const (compile-time folded, but the IdentifierName edge remains).
    public int ConfigureServices() => Used + 10;
}
