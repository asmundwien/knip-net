namespace CatE.E11;

// E11: index-from-end obj[^1] and range obj[1..] on a custom type. The compiler lowers these to
// Length + indexer (for ^1) and Length + Slice (for 1..), with no IdentifierName at the use site.
// CORRECT behavior: Length and Slice ALIVE.
public sealed class Segment
{
    private readonly int[] _data = { 10, 20, 30, 40 };

    // ALIVE (hypothesis): read by both ^index and range lowering.
    public int Length => _data.Length;

    // ALIVE (hypothesis): used by ^1 lowering.
    public int this[int i] => _data[i];

    // ALIVE (hypothesis): used by 1.. range lowering.
    public Segment Slice(int start, int length) => this;

    // DEAD SIBLING: same-shaped method never used by index/range lowering -> must be flagged.
    public Segment SubList(int start, int length) => this;
}

public sealed class Root
{
    public int ConfigureServices()
    {
        var seg = new Segment();
        var last = seg[^1];   // uses Length + indexer
        var tail = seg[1..];  // uses Length + Slice
        return last + tail.Length;
    }
}
