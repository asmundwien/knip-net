namespace CatC.C2;

// C2: overload resolution FAILS because the argument's type is unresolved (Undeclared is an error
// type - it is never declared anywhere). Invariant #3: with no single winner, ReferenceWalker keeps
// EVERY candidate alive rather than guessing, so neither overload is flagged.
public sealed class Sample
{
    public void ConfigureServices()
    {
        // Undeclared is an undeclared identifier -> error type -> overload resolution fails.
        // info.Symbol is null; info.CandidateSymbols holds both Handle overloads -> both kept alive.
        Handle(Undeclared);
    }

    // ALIVE (invariant #3): candidate of the failed resolution.
    public void Handle(int value) { }

    // ALIVE (invariant #3): candidate of the failed resolution.
    public void Handle(string value) { }

    // DEAD SIBLING: a genuinely-dead overload of a DIFFERENT method, never referenced at all.
    public void Other(int value) { }
}
