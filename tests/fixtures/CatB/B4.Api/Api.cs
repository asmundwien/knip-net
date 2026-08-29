namespace CatB.B4;

// B4: this project's NAME matches a `publicApiProjects` glob passed in config, so every externally
// visible symbol here is treated as a root (a consumed contract). The class itself has no in-solution
// use site; liveness comes only from the publicApiProjects rule.
public sealed class Contract
{
    // B4 NOT-FLAGGED: unused PUBLIC member. Unreferenced in the whole solution, but rooted because
    // this is a publicApiProjects match -> must NOT be reported.
    public void UnusedPublicApi() { }

    // B4 DEAD SIBLING: a PRIVATE unused member is NOT externally visible, so publicApiProjects does
    // not root it. With no caller it is unreachable -> flagged. Proves the rule is scoped to the
    // public surface, not "everything in a matching project".
    private void UnusedPrivate() { }
}

public sealed class RootedHost
{
    public void ConfigureServices() => _ = new InternalContainer.PublicNested();
}

internal static class InternalContainer
{
    public sealed class PublicNested
    {
        public void PublicButContained() { }
    }
}
