namespace CatA.A7;

// A7: an entirely dead type is reported once (the outermost symbol); its members are NOT separately
// reported (ShouldReport suppresses members whose containing type is dead).
public sealed class DeadType
{
    public void Method() { }
    public int Property { get; set; }
    public int Field;
}

// A live type in the same namespace so the namespace isn't entirely dead, giving the run a root
// and proving DeadType's report isn't a whole-namespace artifact.
public sealed class LiveType
{
    public void ConfigureServices() { }
}
