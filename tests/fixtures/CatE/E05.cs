namespace CatE.E05;

// E5: foreach over a pattern-based (duck-typed) enumerable. The GetEnumerator / MoveNext / Current
// members are invoked by the foreach lowering with no IdentifierName at the use site.
// CORRECT behavior: GetEnumerator, MoveNext, Current all ALIVE.
public sealed class Numbers
{
    // ALIVE (hypothesis): bound by foreach pattern below.
    public Enumerator GetEnumerator() => new();

    // DEAD SIBLING: same-shaped method not part of the pattern, never called -> must be flagged.
    public Enumerator GetSomethingElse() => new();

    public struct Enumerator
    {
        private int _i;

        // ALIVE (hypothesis): foreach pattern members.
        public bool MoveNext() => _i++ < 3;
        public int Current => _i;
    }
}

public sealed class Root
{
    public int ConfigureServices()
    {
        var total = 0;
        foreach (var n in new Numbers())
            total += n;
        return total;
    }
}
