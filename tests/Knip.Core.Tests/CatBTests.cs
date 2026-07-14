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

    // B6 — DECISION (skipped). Two projects declare an IDENTICAL namespace+type+signature
    // (CatB.B6.Duplicate.Collide). Doc-comment IDs carry no assembly, so both copies collapse to ONE
    // graph node. Project X reaches Collide from a rooted entry point; project Y has no use site.
    // OBSERVED under default config: NO CatB.B6 finding — the shared node is live (X's use), so Y's
    // otherwise-dead copy is also considered live. A collision can only confer EXTRA liveness, i.e. a
    // possible false NEGATIVE, which is aligned with invariant #3.8 (false positives are the risk).
    // Also note DeadCodeAnalyzer.state.Declared is keyed by id with TryAdd, so only ONE of the two
    // Duplicate symbols is ever a reporting candidate regardless of liveness. This test pins the
    // observed empty finding set; a human decides whether merging identical cross-project nodes is
    // the intended contract.
    [Trait("status", "decision")]
    [Fact(Skip = "B6 — decision pending: doc-comment-ID collision across projects")]
    public async Task B6_identical_signature_collision_confers_extra_liveness()
    {
        var findings = await FindingsIn("CatB.B6");
        Assert.Empty(findings);
    }
}
