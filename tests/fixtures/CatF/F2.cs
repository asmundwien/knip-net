namespace CatF.F2;

// F2: a type whose name matches an explicitly configured "*Controller" glob is a root, as are its
// externally visible members. Built-in framework entry types use convention-specific plugin handling.
public sealed class FooController
{
    // ALIVE (root): public member of the explicitly configured entry type.
    public void Index() { }

    // DEAD SIBLING: a private member is not externally visible, so the broad custom entry-point rule
    // does not root it.
    private void Helper() { }
}

// DEAD SIBLING: a plain type does not match the explicit *Controller pattern.
public sealed class FooService
{
    public void Work() { }
}
