namespace CatF.F4;

// F4: a type implementing a configured interface is a root (and its public members). Interfaces match
// by FULLY-QUALIFIED display string, so a LOCAL interface works once "CatF.F4.IHostedService" is passed
// in EntryPoints.ImplementedInterfaces. (The test passes ImplementedInterfaces = ["CatF.F4.IHostedService"].)
public interface IHostedService
{
    void Start();
}

public sealed class Worker : IHostedService
{
    // ALIVE (root): public member of an implementer of the configured interface.
    // (Also an interface implementation, which ShouldReport never flags regardless — the rooting of the
    // TYPE is what F4 proves; the private sibling below carries the dead-sibling contrast.)
    public void Start() { }

    // DEAD SIBLING: private, non-implementing, uncalled -> flagged. Proves it is the interface match
    // (not "the whole file") that roots Worker, and that only externally-visible members are rooted.
    private void Prepare() { }
}

// DEAD SIBLING: does NOT implement IHostedService -> not rooted, uncalled -> flagged (outermost).
public sealed class Bystander
{
    public void Idle() { }
}
