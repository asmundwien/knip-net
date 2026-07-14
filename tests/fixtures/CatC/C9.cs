namespace CatC.C9;

// C9: a type used ONLY in typeof(Foo) / is Foo / as Foo expressions. Each such use site carries an
// IdentifierName bound to the type -> Foo is ALIVE, though never instantiated or inherited.
public sealed class Foo { }

// DEAD SIBLING: same-shaped type, never appears in typeof/is/as or anywhere -> flagged.
public sealed class Unused { }

public sealed class Sample
{
    public bool ConfigureServices(object obj)
    {
        var t = typeof(Foo);
        var casted = obj as Foo;
        return obj is Foo && casted is null && t is not null;
    }
}
