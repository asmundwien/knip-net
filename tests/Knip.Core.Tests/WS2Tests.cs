using Knip.Core.Model;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// WS2 — unused &lt;ProjectReference&gt; detection. A project reference is UNUSED when no symbol in the
/// referencing project touches any symbol in the referenced project's assembly. False positives are
/// the product risk (invariant #8), so the fixture pins BOTH what IS and what is NOT flagged.
///
/// Fixture (tests/fixtures/WS2, a 4-project solution, never in Knip.slnx):
///   Consumer  -> UsedLib   : Consumer.Main calls Greeter.Hello() (real edge)          -> NOT flagged
///   Consumer  -> UnusedLib : no Consumer symbol ever touches an UnusedLib type         -> FLAGGED
///   Consumer  -> HazardLib : Consumer uses an INTERNAL type via [InternalsVisibleTo]   -> NOT flagged
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class WS2Tests
{
    private const string Category = "WS2";

    private static async Task<IReadOnlyList<Finding>> ProjectReferenceFindingsAsync()
    {
        var result = await FixtureRunner.RunAsync(Category);
        return result.Findings
            .Where(f => f.Kind == FindingKind.UnusedProjectReference)
            .ToList();
    }

    [Fact] // The reference to the never-used project IS reported.
    public async Task Unused_project_reference_is_reported()
    {
        var findings = await ProjectReferenceFindingsAsync();

        var unused = Assert.Single(
            findings, f => f.Project == "WS2.Consumer" && f.ReferencedProject == "WS2.UnusedLib");
        Assert.Equal(FindingKind.UnusedProjectReference, unused.Kind);
        Assert.Equal("WS2.UnusedLib", unused.Symbol);
        Assert.Equal("project reference", unused.SymbolKind);
        // Points at the referencing .csproj, no line/column.
        Assert.EndsWith("WS2.Consumer.csproj", unused.FilePath);
        Assert.Equal(0, unused.Line);
        Assert.Equal(0, unused.Column);
    }

    [Fact] // The reference whose types Consumer actually uses is NOT reported (false-positive guard).
    public async Task Used_project_reference_is_not_reported()
    {
        var findings = await ProjectReferenceFindingsAsync();

        Assert.DoesNotContain(findings, f => f.ReferencedProject == "WS2.UsedLib");
    }

    [Fact] // HAZARD: an internals-only dependency (via [InternalsVisibleTo]) is REAL usage, not flagged.
    public async Task Internals_visible_reference_is_conservatively_not_reported()
    {
        var findings = await ProjectReferenceFindingsAsync();

        Assert.DoesNotContain(findings, f => f.ReferencedProject == "WS2.HazardLib");
    }

    [Fact] // The ONLY unused-reference finding for the whole fixture is Consumer -> UnusedLib.
    public async Task Exactly_one_unused_project_reference_across_the_solution()
    {
        var findings = await ProjectReferenceFindingsAsync();

        var only = Assert.Single(findings);
        Assert.Equal("WS2.Consumer", only.Project);
        Assert.Equal("WS2.UnusedLib", only.ReferencedProject);
    }
}
