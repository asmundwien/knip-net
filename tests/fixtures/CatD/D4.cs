namespace CatD.D4;

// D4: an interface with ZERO references anywhere is flagged as an unused type. The interface node has
// no incoming edge (nothing implements it, nothing references it), so it is unreachable and reported.
//
// Mechanism: this is a straight DEAD assertion. The used sibling UsedInterface (implemented by a live,
// referenced type) proves the run distinguishes reachable interfaces from dead ones.
public interface IUnusedContract
{
    void Never();
}

// Sibling: a referenced interface stays alive (implemented + a Client is created and used as root).
public interface IUsedContract
{
    void Handle();
}

public sealed class Client : IUsedContract
{
    public void Handle() { }
}

public sealed class Runner
{
    public void ConfigureServices()
    {
        IUsedContract c = new Client();
        c.Handle();
    }
}
