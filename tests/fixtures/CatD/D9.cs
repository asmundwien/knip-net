namespace CatD.D9;

// D9 (FIX #5, false-negative guard): the FIX #5 edge is TYPE-REACHABILITY-GATED — it is
// containingType->override, NOT an unconditional root. A DEAD type that overrides an external virtual
// (object.ToString()) must STAY dead: the type is reported (outermost-only) and nothing keeps its
// override's private helper alive. This proves FIX #5 introduces no false negative (a dead type's
// override, and its callees, remain unreachable).
public sealed class DeadModel
{
    // Override of external object.ToString() on a DEAD type. The type is never referenced, so the
    // type->override edge has an unreachable source -> the override stays dead (unreported per #7).
    public override string ToString() => Helper();

    // The override's callee. Because DeadModel is unreachable, this stays DEAD. Outermost-only reporting
    // means the whole DeadModel TYPE is the single finding (this member is subsumed, not separately
    // reported) — but it is NOT kept alive.
    private string Helper() => "dead";
}

// Anti-vacuous anchor: a live type proving the fixture's roots work at all.
public sealed class Runner
{
    public void ConfigureServices() { }
}
