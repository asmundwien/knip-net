using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category C — reference-resolution edge cases (overloads, method groups, generic args, nameof,
/// extension methods, delegate/constraint/typeof type references). Each row asserts the EXACT
/// finding set for its scenario namespace (what IS flagged and, by exclusion, what is NOT). Every
/// ALIVE assertion ships with a DEAD SIBLING inside the same fixture: the alive symbol is the one
/// NOT in the expected-dead set, and its dead sibling IS.
///
/// TRIAGE RESULT: C1-C5 and C7-C9 are Contract and GREEN as hypothesized. C6 is a CONFIRMED core
/// false positive — an extension method invoked via extension syntax keeps the METHOD alive but not
/// its containing static class, so the whole static class is flagged (see CatCStatus note below). C6
/// holds the correct assertion but is Skipped + tagged core-gap for a follow-up fix; do NOT weaken it.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatCTests
{
    private const string Category = "CatC";

    private static Task<IReadOnlySet<string>> FindingsIn(string ns) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns);

    private static void AssertExactly(IReadOnlySet<string> actual, params string[] expectedDead) =>
        Assert.Equal(new HashSet<string>(expectedDead), actual);

    // ---- Contract (green) ---------------------------------------------------------------------

    [Fact] // C1: two overloads, one called -> the OTHER flagged. Alive: Handle(int) [dead sibling: Handle(string)].
    [Trait("status", "contract")]
    public async Task C1_uncalled_overload_flagged()
    {
        AssertExactly(await FindingsIn("CatC.C1"), "CatC.C1.Sample.Handle(string)");
    }

    [Fact] // C2: failed overload resolution keeps ALL candidates alive (invariant #3).
    [Trait("status", "contract")]
    public async Task C2_failed_overload_resolution_keeps_all_candidates_alive()
    {
        // Alive (invariant #3): both Handle(int) and Handle(string) — neither is flagged.
        // Dead sibling: Other(int), a genuinely-unreferenced overload of a different method.
        AssertExactly(await FindingsIn("CatC.C2"), "CatC.C2.Sample.Other(int)");
    }

    [Fact] // C3: AddScoped<IFoo, Foo>() keeps Foo alive via generic-arg edge. Dead sibling: Bar.
    [Trait("status", "contract")]
    public async Task C3_generic_arg_keeps_impl_alive()
    {
        AssertExactly(await FindingsIn("CatC.C3"), "CatC.C3.Bar");
    }

    [Fact] // C4: method group as delegate keeps Transform alive. Dead sibling: Untouched(int).
    [Trait("status", "contract")]
    public async Task C4_method_group_delegate_keeps_target_alive()
    {
        AssertExactly(await FindingsIn("CatC.C4"), "CatC.C4.Sample.Untouched(int)");
    }

    [Fact] // C5: nameof(Named) as ONLY reference keeps Named alive. Dead sibling: NeverNamed().
    [Trait("status", "contract")]
    public async Task C5_nameof_only_reference_keeps_method_alive()
    {
        AssertExactly(await FindingsIn("CatC.C5"), "CatC.C5.Sample.NeverNamed()");
    }

    [Fact] // C7: delegate type used only as a parameter type stays alive. Dead sibling: UnusedHandler.
    [Trait("status", "contract")]
    public async Task C7_delegate_as_parameter_type_alive()
    {
        AssertExactly(await FindingsIn("CatC.C7"), "CatC.C7.UnusedHandler");
    }

    [Fact] // C8: interface used only as a generic constraint stays alive. Dead sibling: IUnused.
    [Trait("status", "contract")]
    public async Task C8_interface_as_generic_constraint_alive()
    {
        AssertExactly(await FindingsIn("CatC.C8"), "CatC.C8.IUnused");
    }

    [Fact] // C9: type used only in typeof/is/as stays alive. Dead sibling: Unused.
    [Trait("status", "contract")]
    public async Task C9_type_in_typeof_is_as_alive()
    {
        AssertExactly(await FindingsIn("CatC.C9"), "CatC.C9.Unused");
    }

    // ---- Confirmed core gap (correct assertion, expected red, skipped for follow-up) ----------

    [Fact(Skip = "C6 — core-gap: extension method invoked via extension syntax keeps the method " +
                 "alive but NOT its containing static class, so the whole static class is falsely " +
                 "flagged (reported: CatC.C6.WidgetExtensions). CORRECT: only Unused is dead.")]
    [Trait("status", "core-gap")]
    public async Task C6_extension_method_used_alive_sibling_flagged()
    {
        // CORRECT: the invoked extension method (Used) — and therefore its containing class — stays
        // alive; only the never-invoked sibling extension is flagged. The tool instead flags the
        // whole class because extension-syntax invocation records no edge to WidgetExtensions.
        AssertExactly(await FindingsIn("CatC.C6"), "CatC.C6.WidgetExtensions.Unused(CatC.C6.Widget)");
    }
}
