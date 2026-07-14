namespace CatC.C7;

// C7: a delegate type used ONLY as a parameter type. AddSignatureReferences emits a type-reference
// edge from the enclosing member to the delegate type -> the delegate type is ALIVE, even though it
// is never instantiated or invoked.
public delegate void Handler(int value);

// DEAD SIBLING: a same-shaped delegate type used nowhere -> flagged.
public delegate void UnusedHandler(int value);

public sealed class Sample
{
    // The parameter type Handler roots the delegate type via a signature edge from this method,
    // which is itself alive because it is the ConfigureServices entry-point root.
    public void ConfigureServices(Handler handler) { }
}
