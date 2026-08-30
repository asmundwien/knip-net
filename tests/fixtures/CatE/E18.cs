using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CatE.E18;

[CollectionBuilder(typeof(BagBuilder), "Create")]
public sealed class Bag : IEnumerable<int>
{
    public int Count => 2;

    public IEnumerator<int> GetEnumerator() => throw new System.NotImplementedException();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class BagBuilder
{
    // ALIVE: selected by the collection expression's CollectionBuilder attribute.
    public static Bag Create(System.ReadOnlySpan<int> items) => new();

    // DEAD SIBLING: same builder shape, never selected.
    public static Bag CreateOther(System.ReadOnlySpan<int> items) => new();
}

public sealed class Root
{
    public int ConfigureServices()
    {
        Bag bag = [1, 2];
        return bag.Count;
    }
}
