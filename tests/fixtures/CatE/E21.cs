namespace CatE.E21;

public sealed class Point
{
    // ALIVE: positional pattern matching invokes Deconstruct implicitly.
    public void Deconstruct(out int x, out int y)
    {
        x = 1;
        y = 2;
    }

    // DEAD SIBLING: same shape, not selected by the positional pattern.
    public void DeconstructOther(out int x, out int y)
    {
        x = 3;
        y = 4;
    }
}

public sealed class Root
{
    public bool ConfigureServices(Point point) => point is (1, 2);
}
