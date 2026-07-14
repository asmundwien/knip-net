namespace CatI.I1;

// I1 (KEPT FILE, NOT ignored): the DEAD SIBLING that MUST be flagged, proving the fixture reports.
// A rooted entry point keeps the TYPE alive, so the reported symbol is the METHOD (not the type),
// mirroring CatA's member-dead pattern.
public sealed class KeptDead
{
    // Root (ConfigureServices is a default entry-point name); keeps KeptDead alive.
    public void ConfigureServices() => Used();

    // ALIVE (reached from the root).
    public void Used() { }

    // DEAD SIBLING, in a non-ignored file -> flagged.
    public void KeptDeadMethod() { }
}
