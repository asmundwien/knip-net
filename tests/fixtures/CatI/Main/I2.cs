namespace CatI.I2;

// I2: ignore.symbols FQN glob. IgnoredDeadMethod is dead but matched by "CatI.I2.Sample.Ignored*"
// -> suppressed from the report (yet still DECLARED into the graph — it occupies a node). The
// non-ignored dead sibling ReportedDead proves the fixture reports (anti-vacuous-green). A rooted
// entry point keeps the TYPE alive so the reported symbols are the METHODS, not the type.
public sealed class Sample
{
    // Root (ConfigureServices); keeps Sample alive.
    public void ConfigureServices() => Used();

    public void Used() { }

    // Dead, but its FQ name matches the ignore.symbols glob -> NOT reported.
    public void IgnoredDeadMethod() { }

    // DEAD SIBLING, no glob match -> flagged.
    public void ReportedDead() { }
}
