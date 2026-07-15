using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category G — language-construct fidelity: records, primary ctors, async/iterators, nested types,
/// unsafe pointers, consts, expression-bodied members, local functions and compiler-generated symbols.
/// Every row asserts the EXACT finding set for its scenario namespace (what IS flagged and, by
/// exclusion, what is NOT). Every ALIVE assertion ships with a DEAD SIBLING in the same fixture, so a
/// green assertion cannot be vacuous. All rows are Contract (must be GREEN) — G6 (enum members) was
/// promoted 2026-07-15 by WS-enum from a skip-tagged decision to a member-level dead-code contract.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatGTests
{
    private const string Category = "CatG";

    private static Task<IReadOnlySet<string>> FindingsIn(string ns) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns);

    private static void AssertExactly(IReadOnlySet<string> actual, params string[] expectedDead) =>
        Assert.Equal(new HashSet<string>(expectedDead), actual);

    [Fact] // G1: unused LOCAL function NOT reported (member-level tool). Dead sibling: top-level method.
    [Trait("status", "contract")]
    public async Task G1_local_function_not_reported()
    {
        // ALIVE-by-omission (dead sibling): the unused local function `Unused` is absent from findings;
        // only the real member `UnusedMethod` is flagged.
        AssertExactly(await FindingsIn("CatG.G1"), "CatG.G1.Sample.UnusedMethod()");
    }

    [Fact] // G2: unused record flagged; a used record's synthesized members never reported.
    [Trait("status", "contract")]
    public async Task G2_records_synthesized_members_never_reported()
    {
        // Dead sibling: UnusedRecord is flagged (outermost type). UsedRecord's synthesized ctor/Equals/
        // Deconstruct/props/ToString/GetHashCode are ALIVE-by-omission (compiler-generated, exercised).
        AssertExactly(await FindingsIn("CatG.G2"), "CatG.G2.UnusedRecord");
    }

    [Fact] // G3: used primary-constructor class -> no spurious findings; unused member flagged.
    [Trait("status", "contract")]
    public async Task G3_primary_constructor_no_spurious_findings()
    {
        // Dead sibling: only Service.Unused is flagged. The primary ctor and captured `seed` are
        // ALIVE-by-omission (no spurious primary-ctor / parameter finding).
        AssertExactly(await FindingsIn("CatG.G3"), "CatG.G3.Service.Unused()");
    }

    [Fact] // G4: async + iterator methods treated as normal methods.
    [Trait("status", "contract")]
    public async Task G4_async_and_iterator_treated_as_normal()
    {
        // Dead siblings: the unused async/iterator methods are flagged; the used async (UsedAsync) and
        // used iterator (UsedIterator) are ALIVE-by-omission.
        AssertExactly(await FindingsIn("CatG.G4"),
            "CatG.G4.Sample.UnusedAsync()",
            "CatG.G4.Sample.UnusedIterator()");
    }

    [Fact] // G5: nested private type used by its outer type -> alive; unused nested type flagged.
    [Trait("status", "contract")]
    public async Task G5_nested_type_used_by_outer_alive()
    {
        // Dead sibling: UnusedNested is flagged; UsedNested is ALIVE-by-omission.
        AssertExactly(await FindingsIn("CatG.G5"), "CatG.G5.Outer.UnusedNested");
    }

    [Fact] // G6: member-level enum dead-code (WS-enum). Unused members flagged; used members alive; whole-dead enum reports the TYPE only.
    [Trait("status", "contract")]
    public async Task G6_enum_members_reported()
    {
        // Promoted 2026-07-15 (WS-enum) from the observed-behavior decision (which pinned an EMPTY set).
        // New CORRECT behavior — enum members are first-class graph nodes:
        //   - Color.Green: unused sibling in a LIVE enum -> flagged (UnusedEnumMember). Color.Red is used
        //     (Color.Red) -> ALIVE-by-omission (anti-vacuous live half of the same enum).
        //   - Access.Execute: unused member of a live [Flags] enum -> flagged. Access.Read/Write are OR'd
        //     into a live composite (their identifiers appear) -> ALIVE-by-omission (bitwise-use judgment).
        //   - enum Unused: WHOLE enum is dead -> the TYPE is reported (outermost-only §3.7); its members
        //     A/B are NOT reported (ALIVE-by-omission relative to the member-level rule).
        AssertExactly(await FindingsIn("CatG.G6"),
            "CatG.G6.Color.Green",
            "CatG.G6.Access.Execute",
            "CatG.G6.Unused");
    }

    [Fact] // G7: constructors / static ctors / finalizers NEVER reported (invariant #7).
    [Trait("status", "contract")]
    public async Task G7_constructors_never_reported()
    {
        // Dead sibling: the ordinary UnusedMethod is flagged, proving the type isn't wholesale
        // suppressed. The instance ctor, static ctor and finalizer are ALIVE-by-omission (never
        // reported even though uncalled).
        AssertExactly(await FindingsIn("CatG.G7"), "CatG.G7.Sample.UnusedMethod()");
    }

    [Fact] // G8: compiler-generated symbols (<Main>$, lambdas, anonymous types) never reported.
    [Trait("status", "contract")]
    public async Task G8_compiler_generated_never_reported()
    {
        // Dead sibling: Greeter.Unused is flagged. The synthesized top-level entry point (<Main>$),
        // lambdas and anonymous types are ALIVE-by-omission (filtered by name '<'/'$' + implicit-decl).
        AssertExactly(await FindingsIn("CatG.G8"), "CatG.G8.Greeter.Unused()");
    }

    [Fact] // G9: pointer parameter type Foo* keeps Foo alive (pointer-unwrap edge); unused type flagged.
    [Trait("status", "contract")]
    public async Task G9_pointer_parameter_keeps_type_alive()
    {
        // Dead sibling: UnusedPayload is flagged; Payload is ALIVE-by-omission (reached only via the
        // Payload* parameter, exercising AddTypeReference's pointer unwrap).
        AssertExactly(await FindingsIn("CatG.G9"), "CatG.G9.UnusedPayload");
    }

    [Fact] // G10: referenced const (compile-time folded) alive; unused const flagged.
    [Trait("status", "contract")]
    public async Task G10_referenced_const_alive()
    {
        // Dead sibling: the Unused const is flagged; the referenced Used const is ALIVE-by-omission
        // (Roslyn still yields an IdentifierName edge despite compile-time folding).
        AssertExactly(await FindingsIn("CatG.G10"), "CatG.G10.Sample.Unused");
    }

    [Fact] // G11: expression-bodied members identical to block-bodied (unused flagged, used alive).
    [Trait("status", "contract")]
    public async Task G11_expression_bodied_members()
    {
        // Dead sibling: the unused expression-bodied method is flagged; the used expression-bodied
        // method and property are ALIVE-by-omission.
        AssertExactly(await FindingsIn("CatG.G11"), "CatG.G11.Sample.UnusedMethod()");
    }
}
