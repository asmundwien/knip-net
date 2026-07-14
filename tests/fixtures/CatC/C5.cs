namespace CatC.C5;

// C5: nameof(Method) is the ONLY reference to the method. nameof binds a real IdentifierName to the
// symbol, so an edge is recorded -> the named method is ALIVE even though it is never invoked.
public sealed class Sample
{
    public string ConfigureServices() => nameof(Named);

    // ALIVE: referenced solely by nameof(Named); never called.
    public void Named() { }

    // DEAD SIBLING: same shape, never named nor called -> flagged.
    public void NeverNamed() { }
}
