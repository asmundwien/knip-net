namespace CatD.D10;

// D10 (external-interface liveness): a REACHABLE type implementing an EXTERNAL interface
// (System.IDisposable, always in the BCL) keeps the implementation member's private callee ALIVE.
// The runtime/framework dispatches Dispose() (via using/finally); nothing in the solution calls it,
// so the interface member is not a graph node (invariant #5) and the impl is unreachable — its
// private callee would cascade to dead. The symmetric external-interface fix adds a TYPE->impl edge
// (mirroring FIX #5 for external virtual overrides): when the type is reachable, the impl and its
// callees are reachable. The impl itself is never reported (invariant #7).
public sealed class Disposer : System.IDisposable
{
    private readonly int _handle = 7;

    // Impl of the EXTERNAL interface member IDisposable.Dispose(). Never referenced in source;
    // reachable via the new type->impl edge because Disposer is reachable. Never reported (#7).
    public void Dispose() => Release();

    // ALIVE: called ONLY from the Dispose() impl. Kept alive by the external-interface type->impl edge.
    private int Release() => _handle;

    // DEAD SIBLING (anti-vacuous): a private helper NOT reached from any live path -> flagged. Proves
    // the type is not wholesale-rooted and only the impl's callees gain liveness.
    private int DeadHelper() => _handle;
}

public sealed class Runner
{
    // Root (default symbolName): news Disposer so the type is reachable and the type->impl edge fires.
    // Deliberately does NOT call Dispose() — a direct call would reach Release() without the fix and
    // make the assertion vacuous.
    public void ConfigureServices()
    {
        var d = new Disposer();
        System.Console.WriteLine(d);
    }
}
