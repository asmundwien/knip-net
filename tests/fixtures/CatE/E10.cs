using System;

namespace CatE.E10;

// E10: LINQ query syntax over a CUSTOM query provider. `from`/`where`/`select`/`from...from` bind to
// user-defined Where / Select / SelectMany with no IdentifierName at the method's use site.
// CORRECT behavior: Select, Where, SelectMany all ALIVE.
public sealed class Query<T>
{
    private readonly T _value;
    public Query(T value) => _value = value;

    // ALIVE (hypothesis): bound by `select` in query syntax.
    public Query<TResult> Select<TResult>(Func<T, TResult> selector) => new(selector(_value));

    // ALIVE (hypothesis): bound by `where` in query syntax.
    public Query<T> Where(Func<T, bool> predicate) => this;

    // ALIVE (hypothesis): bound by a second `from` in query syntax.
    public Query<TResult> SelectMany<TCollection, TResult>(
        Func<T, Query<TCollection>> collectionSelector,
        Func<T, TCollection, TResult> resultSelector) =>
        new(resultSelector(_value, collectionSelector(_value)._value));

    public T Value => _value;

    // DEAD SIBLING: same-shaped LINQ-looking method never bound by any query clause -> flagged.
    public Query<TResult> GroupBy<TResult>(Func<T, TResult> keySelector) => new(keySelector(_value));
}

public sealed class Root
{
    public int ConfigureServices()
    {
        var q =
            from x in new Query<int>(1)
            from y in new Query<int>(2)
            where x < y
            select x + y;
        return q.Value;
    }
}
