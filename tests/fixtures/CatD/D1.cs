namespace CatD.D1;

// D1: an interface member invoked through an interface-typed reference keeps the CONCRETE
// implementation ALIVE (polymorphism edge IGreeter.Greet -> Greeter.Greet) and, per invariant #7,
// the implementation is NEVER reported even though it is only reached via the interface.
//
// The impl type Greeter is referenced by name at the `new Greeter()` site so its type node is alive;
// this test proves the MEMBER (Greet) is not falsely flagged. Mechanism: RED-FLIP — remove the
// `g.Greet()` call and the impl becomes unreachable (but still unreported, being an interface impl),
// so the mutation-check sibling is the ordinary uncalled method UnusedHelper.
public interface IGreeter
{
    void Greet();
}

public sealed class Greeter : IGreeter
{
    // ALIVE: interface impl reached via IGreeter.Greet polymorphism edge; never reported anyway.
    public void Greet() { }

    // DEAD SIBLING (mutation check): ordinary uncalled method -> flagged.
    public void UnusedHelper() { }
}

public sealed class Runner
{
    // Root: default entry-point symbol name; roots Runner and reaches IGreeter + Greeter.
    public void ConfigureServices()
    {
        IGreeter g = new Greeter();
        g.Greet();
    }
}
