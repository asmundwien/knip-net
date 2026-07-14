using System.Collections;

namespace CatE.E08;

// E8: collection initializer new C { 1, 2 } lowers to Add(...) calls with no IdentifierName at the
// use site. (Requires IEnumerable + an accessible Add.) CORRECT behavior: Add ALIVE.
public sealed class Bag : IEnumerable
{
    private readonly System.Collections.Generic.List<int> _items = new();

    // ALIVE (hypothesis): invoked by the collection-initializer lowering.
    public void Add(int x) => _items.Add(x);

    // DEAD SIBLING: an overload never used by the initializer -> must be flagged.
    public void Add(string s) => _items.Add(s.Length);

    public IEnumerator GetEnumerator() => _items.GetEnumerator();
}

public sealed class Root
{
    public Bag ConfigureServices() => new() { 1, 2, 3 };
}
