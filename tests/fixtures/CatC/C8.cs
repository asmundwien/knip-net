namespace CatC.C8;

// C8: an interface used ONLY as a generic constraint `where T : IFoo`. The constraint type becomes a
// signature-reference edge from the constrained method -> IFoo is ALIVE, though never otherwise used.
public interface IFoo { }

// DEAD SIBLING: same-shaped interface, never a constraint nor otherwise referenced -> flagged.
public interface IUnused { }

public sealed class Sample
{
    // ConfigureServices is the root; its type-parameter constraint references IFoo.
    public void ConfigureServices<T>() where T : IFoo { }
}
