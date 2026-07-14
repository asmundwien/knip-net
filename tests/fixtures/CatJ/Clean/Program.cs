namespace CatJ.Clean;

// A CLEAN solution: every symbol is reachable from the rooted entry point, so Knip reports
// zero findings and the CLI exits 0. Main is a default root name.
public sealed class Program
{
    public static void Main() => new Program().Run();

    // ALIVE: called from the root.
    public void Run() => Helper();

    // ALIVE: called transitively from the root.
    private void Helper() { }
}
