using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category E — implicitly-invoked members (no IdentifierName/GenericName/object-creation node at the
/// use site). Each test asserts the CORRECT behavior: the implicitly-used member is ALIVE (i.e. NOT
/// in the finding set), and its dead sibling IS flagged. Every ALIVE assertion carries a dead sibling.
///
/// TRIAGE RESULT (see CatEStatus below): E01-E11 are CONFIRMED G-core false positives — the tool
/// flags the live member. Those tests hold the correct assertion but are Skipped + tagged core-gap;
/// each becomes a WS1b fix task. E12 and E13 already pass (IdentifierName exists at the use site) and
/// are shipped as GREEN Contract tests, confirming the hypothesis.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatETests
{
    private const string Category = "CatE";

    private static Task<IReadOnlySet<string>> FindingsIn(string ns) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns);

    private static void AssertExactly(IReadOnlySet<string> actual, params string[] expectedDead) =>
        Assert.Equal(new HashSet<string>(expectedDead), actual);

    // ---- Confirmed G-core (correct assertion, expected red, skipped for WS1b) ----------------

    [Fact]
    [Trait("status", "contract")]
    public async Task E01_indexer_alive()
    {
        // CORRECT: the indexer stays alive; only the unused method is flagged.
        AssertExactly(await FindingsIn("CatE.E01"), "CatE.E01.Box.Unused()");
    }

    [Fact]
    [Trait("status", "contract")]
    public async Task E02_operator_plus_alive()
    {
        AssertExactly(await FindingsIn("CatE.E02"),
            "CatE.E02.Money.operator -(CatE.E02.Money, CatE.E02.Money)");
    }

    [Fact]
    [Trait("status", "contract")]
    public async Task E03_implicit_conversion_alive()
    {
        AssertExactly(await FindingsIn("CatE.E03"),
            "CatE.E03.Celsius.explicit operator double(CatE.E03.Celsius)");
    }

    [Fact]
    [Trait("status", "contract")]
    public async Task E04_equality_operators_alive()
    {
        // CORRECT: both == and != stay alive; nothing in this scenario is dead.
        AssertExactly(await FindingsIn("CatE.E04"));
    }

    [Fact]
    [Trait("status", "contract")]
    public async Task E05_foreach_pattern_alive()
    {
        // CORRECT: GetEnumerator + the Enumerator type stay alive (MoveNext/Current live within it);
        // only the unused GetSomethingElse method is flagged.
        AssertExactly(await FindingsIn("CatE.E05"), "CatE.E05.Numbers.GetSomethingElse()");
    }

    [Fact]
    [Trait("status", "contract")]
    public async Task E06_awaitable_alive()
    {
        // CORRECT: GetAwaiter + the Awaiter type stay alive (IsCompleted/GetResult live within it).
        AssertExactly(await FindingsIn("CatE.E06"));
    }

    [Fact]
    [Trait("status", "contract")]
    public async Task E07_pattern_dispose_alive()
    {
        AssertExactly(await FindingsIn("CatE.E07"), "CatE.E07.Scope.Close()");
    }

    [Fact]
    [Trait("status", "contract")]
    public async Task E08_collection_initializer_add_alive()
    {
        AssertExactly(await FindingsIn("CatE.E08"), "CatE.E08.Bag.Add(string)");
    }

    [Fact]
    [Trait("status", "contract")]
    public async Task E09_deconstruct_alive()
    {
        AssertExactly(await FindingsIn("CatE.E09"), "CatE.E09.Point.DeconstructOther(int, int)");
    }

    [Fact]
    [Trait("status", "contract")]
    public async Task E10_linq_query_methods_alive()
    {
        AssertExactly(await FindingsIn("CatE.E10"),
            "CatE.E10.Query<T>.GroupBy<TResult>(System.Func<T, TResult>)");
    }

    [Fact]
    [Trait("status", "contract")]
    public async Task E11_index_range_members_alive()
    {
        // CORRECT: indexer + Slice + Length stay alive; only the unused SubList is flagged.
        AssertExactly(await FindingsIn("CatE.E11"), "CatE.E11.Segment.SubList(int, int)");
    }

    // ---- Contract (green, hypothesis confirmed: IdentifierName exists at the use site) --------

    [Fact] // E12: object initializer new Foo { Bar = 1 } -> Bar alive; Baz sibling flagged.
    [Trait("status", "contract")]
    public async Task E12_object_initializer_property_alive()
    {
        AssertExactly(await FindingsIn("CatE.E12"), "CatE.E12.Foo.Baz");
    }

    [Fact] // E13: event subscribed with += and raised -> Ping alive; Unused event flagged.
    [Trait("status", "contract")]
    public async Task E13_event_subscription_alive()
    {
        AssertExactly(await FindingsIn("CatE.E13"), "CatE.E13.Publisher.Unused");
    }
}
