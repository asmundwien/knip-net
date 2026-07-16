namespace CatD.D11;

// D11 (external-interface liveness, false-negative guard): the type->impl edge is
// TYPE-REACHABILITY-GATED — it is containingType->impl, NOT an unconditional root. A DEAD type that
// implements the same external interface (System.IDisposable) must STAY dead: the type is reported
// (outermost-only) and nothing keeps its impl's private callee alive. This proves the new edge
// introduces no false negative (a dead type's impl, and its callees, remain unreachable).
public sealed class DeadDisposer : System.IDisposable
{
    // Impl of external IDisposable.Dispose() on a DEAD type. The type is never referenced, so the
    // type->impl edge has an unreachable source -> the impl stays dead (unreported per #7).
    public void Dispose() => Helper();

    // The impl's callee. Because DeadDisposer is unreachable, this stays DEAD. Outermost-only reporting
    // means the whole DeadDisposer TYPE is the single finding (this member is subsumed) — but it is
    // NOT kept alive.
    private int Helper() => 0;
}

// Anti-vacuous anchor: a live type proving the fixture's roots work at all.
public sealed class Runner
{
    public void ConfigureServices() { }
}
