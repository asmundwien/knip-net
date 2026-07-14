namespace CatD.D6;

// D6: an override-of-override chain (Base.M virtual -> Mid.M override -> Leaf.M override) in which the
// BASE member is invoked keeps the WHOLE chain alive. Polymorphism edges Base.M->Mid.M and
// Mid.M->Leaf.M propagate reachability down the chain; Mid.M and Leaf.M are overrides and thus never
// reported (invariant #7), and Base.M is reached so it is not reported either.
//
// Mechanism: RED-FLIP proves chain-aliveness (dropping the b.M() call makes the chain unreachable,
// though the overrides remain unreported). Mutation-check sibling = ordinary uncalled UnusedHelper on
// Leaf, which IS flagged, proving the run is not vacuously empty.
public abstract class Base
{
    public abstract void M();
}

public class Mid : Base
{
    public override void M() { }
}

public sealed class Leaf : Mid
{
    public override void M() { }

    // DEAD SIBLING (mutation check): ordinary uncalled method -> flagged.
    public void UnusedHelper() { }
}

public sealed class Runner
{
    public void ConfigureServices()
    {
        // Create Leaf (keeps Leaf + base chain types alive) and invoke via the base member.
        Base b = new Leaf();
        b.M();
    }
}
