namespace CatE.E17;

public sealed class Point
{
    // ALIVE: foreach variable deconstruction invokes this method implicitly.
    public void Deconstruct(out int x, out int y)
    {
        x = 1;
        y = 2;
    }

    // DEAD SIBLING: same shape, not selected for deconstruction.
    public void DeconstructOther(out int x, out int y)
    {
        x = 3;
        y = 4;
    }
}

public sealed class Root
{
    public int ConfigureServices()
    {
        var sum = 0;
        foreach (var (x, y) in new[] { new Point() })
            sum += x + y;
        return sum;
    }
}
