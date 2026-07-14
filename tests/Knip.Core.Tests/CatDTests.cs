using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category D — polymorphism &amp; reporting invariants (invariant #7). Each row asserts the EXACT
/// finding set for its scenario namespace: what IS flagged and, by exclusion, what is NOT. Overrides,
/// interface implementations and constructors are NEVER reported; abstractions that are reached keep
/// their implementations/overrides alive. Every ALIVE assertion carries either a DEAD SIBLING inside
/// the same fixture (an ordinary uncalled method — a polymorphic member can't be a dead sibling since
/// invariant #7 suppresses it) or is proven by RED-FLIP; the mechanism is named per row.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatDTests
{
    private const string Category = "CatD";

    private static Task<IReadOnlySet<string>> FindingsIn(string ns) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns);

    private static void AssertExactly(IReadOnlySet<string> actual, params string[] expectedDead) =>
        Assert.Equal(new HashSet<string>(expectedDead), actual);

    [Fact] // D1: interface member called via interface ref -> impl (Greeter.Greet) ALIVE, never reported.
    [Trait("status", "contract")]
    public async Task D1_interface_impl_via_interface_ref_alive()
    {
        // Greeter.Greet is reached only through the IGreeter.Greet polymorphism edge and is an
        // interface impl -> never reported. Mechanism: RED-FLIP (remove g.Greet() -> still unreported).
        // Dead-sibling mutation check: Greeter.UnusedHelper() (ordinary uncalled method) IS flagged.
        AssertExactly(await FindingsIn("CatD.D1"), "CatD.D1.Greeter.UnusedHelper()");
    }

    [Fact] // D2: override called via base-class ref -> override (Dog.Speak) ALIVE, never reported.
    [Trait("status", "contract")]
    public async Task D2_override_via_base_ref_alive()
    {
        // Dog.Speak reached via Animal.Speak polymorphism edge; an override -> never reported.
        // Mechanism: RED-FLIP. Dead-sibling mutation check: Dog.UnusedHelper() IS flagged.
        AssertExactly(await FindingsIn("CatD.D2"), "CatD.D2.Dog.UnusedHelper()");
    }

    [Fact] // D3: explicit interface implementation (Widget.IWidget.Render) NEVER reported.
    [Trait("status", "contract")]
    public async Task D3_explicit_interface_impl_never_reported()
    {
        // The explicit impl Widget.IWidget.Render() must NOT appear even though it is never dispatched.
        // The unused interface MEMBER IWidget.Render() IS a genuine finding (nothing invokes it) — that
        // is correct, not a false positive on the impl. Dead-sibling mutation check: Widget.UnusedHelper().
        AssertExactly(await FindingsIn("CatD.D3"),
            "CatD.D3.IWidget.Render()",
            "CatD.D3.Widget.UnusedHelper()");
    }

    [Fact] // D4: interface with ZERO references -> flagged; a referenced interface stays alive.
    [Trait("status", "contract")]
    public async Task D4_unreferenced_interface_flagged()
    {
        // IUnusedContract has no incoming edge -> flagged. Sibling IUsedContract (implemented by the
        // live Client, dispatched via c.Handle()) is NOT flagged, distinguishing dead from live.
        AssertExactly(await FindingsIn("CatD.D4"), "CatD.D4.IUnusedContract");
    }

    [Fact] // D5: derived type used -> base type alive via BaseType edge; base-less Orphan flagged.
    [Trait("status", "contract")]
    public async Task D5_base_alive_via_basetype_edge()
    {
        // Base is never referenced by name; it stays alive only through Derived's BaseType edge.
        // Dead sibling: Orphan (an unused base-less type) IS flagged, carrying the Base-alive assertion.
        AssertExactly(await FindingsIn("CatD.D5"), "CatD.D5.Orphan");
    }

    [Fact] // D6: override-of-override chain, base member used -> whole chain alive.
    [Trait("status", "contract")]
    public async Task D6_override_chain_alive()
    {
        // Base.M reached via b.M(); polymorphism edges keep Mid.M and Leaf.M (overrides) alive; none
        // reported. Mechanism: RED-FLIP. Dead-sibling mutation check: Leaf.UnusedHelper() IS flagged.
        AssertExactly(await FindingsIn("CatD.D6"), "CatD.D6.Leaf.UnusedHelper()");
    }

    [Fact] // D7: unused attribute class flagged; applied attribute class alive (own used/unused pair).
    [Trait("status", "contract")]
    public async Task D7_unused_attribute_flagged_applied_alive()
    {
        // UnusedAttribute is applied nowhere -> flagged. UsedAttribute is applied to the rooted Runner
        // type -> alive (not in the set). The pair is the mutation check (identical minus application).
        AssertExactly(await FindingsIn("CatD.D7"), "CatD.D7.UnusedAttribute");
    }
}
