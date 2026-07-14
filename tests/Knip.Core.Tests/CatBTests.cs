using Knip.Core.Configuration;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category B — cross-project reachability, visibility, and config-driven rooting. Rows B1..B5 are
/// Contract (must be GREEN) and assert the EXACT finding set for their scenario namespace (what IS
/// flagged and, by exclusion, what is NOT). Every ALIVE assertion ships with a DEAD SIBLING in the
/// same fixture. B6 is a DECISION row (skipped): it documents the observed doc-comment-ID collision
/// behavior for a human to rule on — it pins no "correct" answer.
///
/// The fixture is one multi-project solution (tests/fixtures/CatB/Fixture.slnx). Each test runs the
/// whole solution and filters to its own namespace, so a per-test config (PublicApiProjects /
/// TreatAllPublicAsUsed) that changes rooting for OTHER namespaces is harmless to this row's assert.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatBTests
{
    private const string Category = "CatB";

    private static Task<IReadOnlySet<string>> FindingsIn(string ns, KnipConfig? config = null) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns, config);

    // B1 + B2 share one namespace (CatB.B1B2): B1 asserts the cross-project-used method is ALIVE,
    // B2 is its dead sibling in the same lib. One exact-set assertion covers both rows.
    //
    // B1 (ALIVE, pins invariant #1): Widget.UsedFromConsumer is called ONLY from the Consumer
    //   project's ConfigureServices. The cross-project edge resolves by doc-comment ID; if it
    //   regressed, UsedFromConsumer would appear here (RED) — a critical bug, not a re-tag.
    // B2 (DEAD SIBLING): Widget.UnusedInLib — identical public shape, no caller anywhere -> flagged.
    [Fact]
    public async Task B1_B2_cross_project_used_alive_unused_sibling_flagged()
    {
        var findings = await FindingsIn("CatB.B1B2");
        Assert.Equal(
            new HashSet<string> { "CatB.B1B2.Widget.UnusedInLib()" },
            findings);
    }

    // B3 (ALIVE): Engine.InternalUsedByFriend — an INTERNAL member bound cross-project through
    //   [InternalsVisibleTo]. Accessibility does not change the graph key, so it stays alive.
    // B3 (DEAD SIBLING): Engine.InternalUnused — identical internal shape, no caller -> flagged.
    [Fact]
    public async Task B3_internalsvisibleto_used_alive_unused_internal_sibling_flagged()
    {
        var findings = await FindingsIn("CatB.B3");
        Assert.Equal(
            new HashSet<string> { "CatB.B3.Engine.InternalUnused()" },
            findings);
    }

    // B4: publicApiProjects glob roots the public surface of a MATCHING project.
    //   ALIVE (NOT flagged): Contract.UnusedPublicApi — unreferenced public member, rooted because
    //     CatB.B4.Api matches the "*.B4.Api" glob.
    //   DEAD SIBLING: Contract.UnusedPrivate — private, not externally visible, so the rule does not
    //     root it; unreferenced -> flagged. Proves the rule is scoped to the public surface only.
    [Fact]
    public async Task B4_publicapiprojects_public_not_flagged_private_sibling_flagged()
    {
        var config = new KnipConfig();
        config.Roots.PublicApiProjects.Add("*.B4.Api");

        var findings = await FindingsIn("CatB.B4", config);
        Assert.Equal(
            new HashSet<string> { "CatB.B4.Contract.UnusedPrivate()" },
            findings);
    }

    // B5: treatAllPublicAsUsed roots every externally visible symbol solution-wide.
    //   ALIVE (NOT flagged): Surface.UnusedPublic — unreferenced public member, rooted by the flag.
    //   DEAD SIBLING: Surface.UnusedPrivate — private, not rooted by the flag; unreferenced -> flagged.
    [Fact]
    public async Task B5_treatallpublicasused_public_not_flagged_private_sibling_flagged()
    {
        var config = new KnipConfig { Roots = { TreatAllPublicAsUsed = true } };

        var findings = await FindingsIn("CatB.B5", config);
        Assert.Equal(
            new HashSet<string> { "CatB.B5.Surface.UnusedPrivate()" },
            findings);
    }

    // B6 — CONTRACT. Two projects declare an IDENTICAL namespace+type+signature
    // (CatB.B6.Duplicate.Collide). Doc-comment IDs carry no assembly, but SymbolId qualifies each key
    // with the DEFINING assembly, so the two copies are DISTINCT graph nodes. Project X (assembly
    // CatB.B6.ProjX) reaches its Duplicate.Collide from a rooted entry point
    // (XStartup.ConfigureServices) — X's whole Duplicate type stays alive. Project Y (assembly
    // CatB.B6.ProjY) has NO use site for its identical copy, so Y's Duplicate type is genuinely dead.
    // ShouldReport rolls a fully-dead type up to the outermost dead symbol, so the finding is the type
    // CatB.B6.Duplicate (Y's copy), not the member. This pins the fix for the former doc-comment-ID
    // collision (previously a false NEGATIVE: X's use kept Y's dead copy alive -> empty finding set).
    [Trait("status", "contract")]
    [Fact]
    public async Task B6_identical_signature_collision_flags_unused_duplicate()
    {
        var findings = await FindingsIn("CatB.B6");
        Assert.Equal(
            new HashSet<string> { "CatB.B6.Duplicate" },
            findings);
    }
}
