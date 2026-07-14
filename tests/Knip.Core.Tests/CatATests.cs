using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category A — reachability fundamentals. All rows are Contract: they must be GREEN. Each row
/// asserts the EXACT finding set for its scenario namespace (what IS flagged and, by exclusion,
/// what is NOT). Every ALIVE assertion ships with a DEAD SIBLING inside the same fixture.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatATests
{
    private const string Category = "CatA";

    private static Task<IReadOnlySet<string>> FindingsIn(string ns) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns);

    [Fact] // A1: unused private method flagged; used private sibling alive.
    public async Task A1_unused_private_method_flagged()
    {
        var findings = await FindingsIn("CatA.A1");
        Assert.Equal(
            new HashSet<string> { "CatA.A1.Sample.UnusedPrivate()" },
            findings);
    }

    [Fact] // A2: unused public method flagged (no public-as-used); used public sibling alive.
    public async Task A2_unused_public_method_flagged()
    {
        var findings = await FindingsIn("CatA.A2");
        Assert.Equal(
            new HashSet<string> { "CatA.A2.Sample.UnusedPublic()" },
            findings);
    }

    [Fact] // A3: root->A->B keeps A,B alive; only C flagged.
    public async Task A3_transitive_chain_only_uncalled_flagged()
    {
        var findings = await FindingsIn("CatA.A3");
        Assert.Equal(
            new HashSet<string> { "CatA.A3.Sample.C()" },
            findings);
    }

    [Fact] // A4: dead island A<->B both flagged; rooted LiveProof alive.
    public async Task A4_dead_island_both_flagged()
    {
        var findings = await FindingsIn("CatA.A4");
        Assert.Equal(
            new HashSet<string> { "CatA.A4.Sample.A()", "CatA.A4.Sample.B()" },
            findings);
    }

    [Fact] // A5: self-recursive with no external caller flagged; recursive rooted sibling alive.
    public async Task A5_self_recursive_no_caller_flagged()
    {
        var findings = await FindingsIn("CatA.A5");
        Assert.Equal(
            new HashSet<string> { "CatA.A5.Sample.Recurse(int)" },
            findings);
    }

    [Fact] // A6: symbol referenced only from dead code is itself flagged (dead code confers no life).
    public async Task A6_referenced_only_from_dead_code_flagged()
    {
        var findings = await FindingsIn("CatA.A6");
        Assert.Equal(
            new HashSet<string> { "CatA.A6.Sample.Dead()", "CatA.A6.Sample.Target()" },
            findings);
    }

    [Fact] // A7: dead type reported once (outermost); its members NOT separately reported.
    public async Task A7_dead_type_outermost_only()
    {
        var findings = await FindingsIn("CatA.A7");
        Assert.Equal(
            new HashSet<string> { "CatA.A7.DeadType" },
            findings);
    }

    [Fact] // A8: partial class/method used -> one node, alive; non-partial sibling flagged.
    public async Task A8_partial_used_alive_sibling_flagged()
    {
        var findings = await FindingsIn("CatA.A8");
        Assert.Equal(
            new HashSet<string> { "CatA.A8.Sample.UnusedMethod()" },
            findings);
    }
}
