namespace CatK7NoTests;

// K7 zero-detection: a single PRODUCTION-named project ("App"), no test-framework assembly reference,
// no testProjects config -> classified production. Running production mode here detects ZERO test
// projects, which must produce a LOUD warning (stderr + reliability.productionModeWarnings) and never
// fail. `Helper` is plain dead (no caller) so a finding still exists — proving analysis ran.
public sealed class Service
{
    // Plain dead: no caller at all -> ordinary UnusedMethod finding in either mode.
    public void Helper() { }
}
