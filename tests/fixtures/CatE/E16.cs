namespace CatE.E16;

public readonly struct Source;

public readonly struct Target
{
    // ALIVE: yield return converts Source to the iterator element type.
    public static implicit operator Target(Source value) => new();

    // DEAD SIBLING: the reverse user-defined conversion is never selected.
    public static explicit operator Source(Target value) => new();
}

public sealed class Root
{
    public System.Collections.Generic.IEnumerable<Target> ConfigureServices()
    {
        yield return new Source();
    }
}
