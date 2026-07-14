namespace CatE.E07;

// E7: pattern-based Dispose on a ref struct in a using statement. The Dispose method is invoked by
// the using lowering with no IdentifierName at the use site (ref struct can't implement IDisposable
// pre-C#8; here it's pure pattern-based). CORRECT behavior: Dispose ALIVE.
public ref struct Scope
{
    // ALIVE (hypothesis): invoked by `using` lowering.
    public void Dispose() { }

    // DEAD SIBLING: same-shaped method never called -> must be flagged.
    public void Close() { }
}

public sealed class Root
{
    public void ConfigureServices()
    {
        using var s = new Scope();
    }
}
