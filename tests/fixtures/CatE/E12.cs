namespace CatE.E12;

// E12: object initializer new Foo { Bar = 1 }. Unlike E1-E11 there IS an IdentifierName ("Bar") at
// the use site, so this is HYPOTHESIZED already-green. CORRECT behavior: the Bar setter/property ALIVE.
public sealed class Foo
{
    // ALIVE (hypothesis): assigned via object initializer below.
    public int Bar { get; set; }

    // DEAD SIBLING: same-shaped property never assigned/read -> must be flagged.
    public int Baz { get; set; }
}

public sealed class Root
{
    public Foo ConfigureServices() => new() { Bar = 1 };
}
