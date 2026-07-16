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

    [Fact] // D8 (FIX #5): a REACHABLE type overriding an EXTERNAL virtual (object.ToString) keeps the
           // private helper it calls ALIVE via the type->override edge; the override itself is never
           // reported (invariant #7); a private helper NOT reached stays flagged (dead-sibling).
    [Trait("status", "contract")]
    public async Task D8_external_override_on_reachable_type_keeps_callee_alive()
    {
        // Model.ToString() overrides object.ToString() (external virtual). FIX #5 edges Model->ToString
        // so, Model being reachable, the override's callee Describe() is reachable. DeadHelper() is not
        // reached from any live path -> flagged, proving the type is not wholesale-rooted.
        AssertExactly(await FindingsIn("CatD.D8"), "CatD.D8.Model.DeadHelper()");
    }

    [Fact] // D9 (FIX #5 false-negative guard): a DEAD type overriding an external virtual stays dead —
           // the type->override edge is type-reachability-gated, so an unreachable type's override (and its
           // callees) remain unreachable. Outermost-only reports the whole DEAD type once.
    [Trait("status", "contract")]
    public async Task D9_external_override_on_dead_type_stays_dead()
    {
        // DeadModel is never referenced, so the FIX #5 edge has an unreachable source: the whole type is
        // reported (outermost-only), NOT kept alive. This proves FIX #5 introduces no false negative.
        AssertExactly(await FindingsIn("CatD.D9"), "CatD.D9.DeadModel");
    }

    [Fact] // D10 (external-interface liveness): a REACHABLE type implementing an EXTERNAL interface
           // (System.IDisposable) keeps the impl's private callee ALIVE via the type->impl edge; the
           // impl itself is never reported (invariant #7); a private helper NOT reached stays flagged.
    [Trait("status", "contract")]
    public async Task D10_external_interface_impl_on_reachable_type_keeps_callee_alive()
    {
        // Disposer.Dispose() implements external IDisposable.Dispose(). The external interface member is
        // not a graph node (#5), so the impl is reachable only via the type->impl edge — Disposer being
        // reachable keeps the override's callee Release() alive. DeadHelper() is unreached -> flagged,
        // proving the type is not wholesale-rooted.
        AssertExactly(await FindingsIn("CatD.D10"), "CatD.D10.Disposer.DeadHelper()");
    }

    [Fact] // D11 (external-interface liveness, false-negative guard): a DEAD type implementing an
           // external interface stays dead — the type->impl edge is type-reachability-gated, so an
           // unreachable type's impl (and its callees) remain unreachable. Outermost-only reports once.
    [Trait("status", "contract")]
    public async Task D11_external_interface_impl_on_dead_type_stays_dead()
    {
        // DeadDisposer is never referenced, so the type->impl edge has an unreachable source: the whole
        // type is reported (outermost-only), NOT kept alive. This proves the new edge adds no false negative.
        AssertExactly(await FindingsIn("CatD.D11"), "CatD.D11.DeadDisposer");
    }
}
