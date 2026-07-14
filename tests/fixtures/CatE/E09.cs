namespace CatE.E09;

// E9: tuple deconstruction `var (a, b) = obj` invokes a user-defined Deconstruct with no
// IdentifierName at the use site. CORRECT behavior: Deconstruct ALIVE.
public sealed class Point
{
    public Point(int x, int y) { X = x; Y = y; }
    public int X { get; }
    public int Y { get; }

    // ALIVE (hypothesis): invoked by the deconstruction below.
    public void Deconstruct(out int x, out int y) { x = X; y = Y; }

    // DEAD SIBLING: same-shaped method never used for deconstruction -> must be flagged.
    public void DeconstructOther(out int a, out int b) { a = X; b = Y; }
}

public sealed class Root
{
    public int ConfigureServices()
    {
        var (a, b) = new Point(3, 4);
        return a + b;
    }
}
