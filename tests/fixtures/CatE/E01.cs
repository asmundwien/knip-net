namespace CatE.E01;

// E1: custom indexer invoked via obj[i]. There is no IdentifierName at the use site, only an
// ElementAccessExpression, so the walker may miss the edge. CORRECT behavior: the indexer is ALIVE.
public sealed class Box
{
    private readonly int[] _data = new int[4];

    // ALIVE (hypothesis): used via this[..] below.
    public int this[int i]
    {
        get => _data[i];
        set => _data[i] = value;
    }

    // DEAD SIBLING: an ordinary method never called -> must be flagged, proving the fixture reports.
    public int Unused() => 0;
}

public sealed class Root
{
    public int ConfigureServices()
    {
        var b = new Box();
        b[0] = 7;        // use-site of the indexer setter
        return b[0];     // use-site of the indexer getter
    }
}
