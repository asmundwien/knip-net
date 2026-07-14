namespace CatA.A1;

// A1: an unused PRIVATE method on a live type is flagged.
// The type is kept alive by the rooted entry point (ConfigureServices is a configured root name),
// so the outermost-dead rule does not suppress the member; only UnusedPrivate is unreachable.
public sealed class Sample
{
    // Root: named ConfigureServices, which is a default entry-point symbol name.
    public void ConfigureServices() => UsedPrivate();

    // DEAD SIBLING: identical shape to UsedPrivate but no caller -> flagged.
    private void UnusedPrivate() { }

    // ALIVE: called from the root.
    private void UsedPrivate() { }
}
