namespace CatJ.WithFindings;

// A solution WITH findings: the rooted entry point keeps Program alive, but the dead siblings
// below (and the dead members in Bravo.cs) are unreachable -> the CLI reports findings, exit 1.
public sealed class Program
{
    public static void Main() => new Program().Used();

    // ALIVE: called from the root.
    public void Used() { }

    // DEAD: no caller -> flagged.
    private void UnusedAlpha() { }

    // DEAD: no caller -> flagged.
    private void UnusedBravo() { }
}
