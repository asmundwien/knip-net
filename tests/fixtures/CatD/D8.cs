namespace CatD.D8;

// D8 (FIX #5): a REACHABLE type overrides an EXTERNAL virtual — object.ToString() (offline, always in
// the BCL). The runtime/framework invokes ToString() polymorphically; it is never referenced in source,
// so invariant #7 keeps it UNREPORTED. But a private helper it calls would cascade to dead unless the
// override is REACHABLE. FIX #5 adds a TYPE->override edge, so when the type is reached the override —
// and its callee — is reached. The type is reached here because Runner news it by name.
public sealed class Model
{
    private readonly int _value = 42;

    // Override of the EXTERNAL virtual object.ToString(). Never reported (invariant #7); reachable via
    // the FIX #5 type->override edge because Model is reachable.
    public override string ToString() => Describe();

    // ALIVE: called ONLY from the ToString() override. Kept alive by FIX #5 (the override is reachable).
    private string Describe() => "Model(" + _value + ")";

    // DEAD SIBLING (anti-vacuous): a private helper NOT reached from any live path -> flagged. Proves the
    // type is not wholesale-rooted and only the override's callees gain liveness.
    private string DeadHelper() => "dead";
}

public sealed class Runner
{
    // Root (default symbolName): keeps Model reachable so the FIX #5 type->override edge fires.
    public void ConfigureServices()
    {
        var m = new Model();
        System.Console.WriteLine(m);
    }
}
