namespace CatD.D5;

// D5: using a DERIVED type keeps its BASE type alive via the BaseType signature edge
// (Derived -> Base in AddSignatureReferences). The base type is never referenced by name directly,
// yet must stay alive. Sibling: a base-less type that is never referenced is flagged.
//
// Mechanism: the alive assertion (Base not reported) is carried by its DEAD SIBLING Orphan, an
// unused base-less type in the same scenario that IS flagged. Base-alive is via BaseType edge from
// the referenced Derived.
public class Base
{
    // ALIVE only via the derived type's BaseType edge; a member here would be its own node, so keep
    // the base empty to isolate the type-level BaseType edge behaviour.
}

public sealed class Derived : Base
{
}

// DEAD SIBLING: a base-less type that nothing references -> flagged as unused type.
public sealed class Orphan
{
}

public sealed class Runner
{
    public void ConfigureServices()
    {
        // Reference Derived by name so Derived is alive; Base must follow via the BaseType edge.
        Derived d = new Derived();
        _ = d;
    }
}
