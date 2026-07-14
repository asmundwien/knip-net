using CatK;

namespace CatK.K7;

// K7 (Decision): how should production mode CLASSIFY a project as a "test project"? Candidates:
//   (a) MSBuild <IsTestProject>true</IsTestProject> property (explicit, but not always set),
//   (b) presence of a test-framework package reference (xunit/nunit/mstest) — offline fixtures have
//       none, so this is unobservable here,
//   (c) a project-name glob (e.g. "*Tests", "*.Test", "*.Tests") — cheap, name-dependent.
// AND: should production mode WARN when ZERO test projects are detected (likely misconfiguration:
// every test-only symbol would flip to a finding, drowning the report)? No answer is pinned here.
// This fixture just provides a plausibly-test-named type; the design question is captured in the
// Skip-tagged decision test and reported to the human.
public sealed class Probe
{
    public void Method() { }
}
