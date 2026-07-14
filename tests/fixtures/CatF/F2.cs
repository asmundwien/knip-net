namespace CatF.F2;

// F2: a type whose name matches the "*Controller" glob (a default NamePattern) is a root, and so are
// its externally-visible members (EvaluateRoots roots IsExternallyVisible members of an entry type).
public sealed class FooController
{
    // ALIVE (root): public member of a *Controller type.
    public void Index() { }

    // DEAD SIBLING: PRIVATE member is NOT externally visible, so it is NOT auto-rooted and, being
    // uncalled, is flagged -> proves only public members of the entry type are rooted.
    private void Helper() { }
}

// DEAD SIBLING: a plain type that does NOT match *Controller, uncalled -> flagged (proves the glob,
// not "everything", is what roots F2).
public sealed class FooService
{
    public void Work() { }
}
