using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category H — the "moat": usages the built-in heuristics CANNOT see (reflection, string type
/// names, scanning DI, serializers, data-binding, XAML/WebForms markup, config, dynamic, generated
/// code). Today the tool FLAGS these symbols as dead — that is EXPECTED (invariant #8: for these
/// rows the current false positive is the whole point). No src/ fix here.
///
/// Most rows are <c>G-moat</c> GAP tests: each asserts the CORRECT EVENTUAL behavior (the invisible
/// symbol ALIVE, its dead sibling still flagged), but is <see cref="FactAttribute.Skip"/>-tagged so a
/// future WS5 plugin flips it green. Every G-moat assertion below was FIRST run un-skipped to CONFIRM
/// it is RED today (the tool flags the moat symbol); the confirmed today-finding is noted per row.
///
/// Exceptions: H3 is a real CONTRACT (verify GREEN — typeof edges are visible); H11 is a DECISION
/// (pins observed behavior, no "correct" answer). Every ALIVE assertion ships with a DEAD SIBLING in
/// the same fixture, so a green (future) assertion cannot be vacuous.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatHTests
{
    private const string Category = "CatH";

    private static Task<IReadOnlySet<string>> FindingsIn(string ns) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns);

    private static void AssertExactly(IReadOnlySet<string> actual, params string[] expectedDead) =>
        Assert.Equal(new HashSet<string>(expectedDead), actual);

    // H1 — CONFIRMED RED TODAY: [Handle(), NeverCalled()] (Handle flagged; reflection is invisible).
    [Fact(Skip = "H1 — WS5: reflection plugin (GetMethod(\"X\").Invoke); mitigation today: ignore.symbols [\"CatH.H1.Service.Handle()\"]")]
    [Trait("status", "moat")]
    public async Task H1_reflection_invoked_member_alive()
    {
        // FUTURE: Handle() ALIVE (reached only via GetMethod("Handle").Invoke); NeverCalled() flagged.
        AssertExactly(await FindingsIn("CatH.H1"), "CatH.H1.Service.NeverCalled()");
    }

    // H2 — CONFIRMED RED TODAY: [Plugin, UnusedPlugin] (Plugin flagged; the string name is invisible).
    [Fact(Skip = "H2 — WS5: reflection plugin (Type.GetType(\"Ns.Foo\")); mitigation today: ignore.symbols [\"CatH.H2.Plugin\"]")]
    [Trait("status", "moat")]
    public async Task H2_string_named_type_alive()
    {
        // FUTURE: Plugin ALIVE (named only in the "CatH.H2.Plugin" string); UnusedPlugin flagged.
        AssertExactly(await FindingsIn("CatH.H2"), "CatH.H2.UnusedPlugin");
    }

    // H3 — CONTRACT (must be GREEN). VERIFIED: Foo is NOT flagged — typeof(Foo) yields a real
    // IdentifierName edge, so non-generic DI with typeof keeps the implementation alive WITHOUT any
    // plugin. The finding set is exactly the interface's uncalled Do() and the never-typeof'd sibling.
    [Fact]
    [Trait("status", "contract")]
    public async Task H3_non_generic_di_typeof_keeps_impl_alive()
    {
        // Foo ALIVE-by-omission (via typeof edge). IFoo.Do() is an uncalled interface member; Foo.Do()
        // is an interface implementation (suppressed). UnreferencedFoo (never in a typeof) is flagged.
        AssertExactly(await FindingsIn("CatH.H3"),
            "CatH.H3.IFoo.Do()",
            "CatH.H3.UnreferencedFoo");
    }

    // H4 — CONFIRMED RED TODAY: [IRequestHandler, MyHandler, UnrelatedType] (MyHandler flagged;
    // assembly scanning is invisible).
    [Fact(Skip = "H4 — WS5: scanning-DI plugin (Scrutor/MediatR/AutoMapper); mitigation today: entryPoints.implementedInterfaces [\"CatH.H4.IRequestHandler\"] / baseTypes")]
    [Trait("status", "moat")]
    public async Task H4_assembly_scanned_handler_alive()
    {
        // FUTURE: MyHandler ALIVE (root via implemented interface -> IRequestHandler alive too);
        // UnrelatedType (not a handler) still flagged.
        AssertExactly(await FindingsIn("CatH.H4"), "CatH.H4.UnrelatedType");
    }

    // H5 — CONFIRMED RED TODAY: [PersonDto.InternalScratch, PersonDto.Name] (Name flagged;
    // serializer reflection is invisible).
    [Fact(Skip = "H5 — WS5: serializer plugin (JSON reflection over DTO props); mitigation today: ignore.namespaces [\"CatH.H5.Dto*\"] / ignore.symbols")]
    [Trait("status", "moat")]
    public async Task H5_serialized_dto_property_alive()
    {
        // FUTURE: PersonDto.Name ALIVE (touched only by the serializer); InternalScratch flagged.
        AssertExactly(await FindingsIn("CatH.H5"), "CatH.H5.PersonDto.InternalScratch");
    }

    // H6 — CONFIRMED RED TODAY: [MyComponent.Title, MyComponent.Unbound, ParameterAttribute]
    // (Title flagged; markup binding is invisible).
    [Fact(Skip = "H6 — WS5: Blazor plugin ([Parameter] set from markup); mitigation today: entryPoints.attributes [\"Parameter\"]")]
    [Trait("status", "moat")]
    public async Task H6_blazor_parameter_property_alive()
    {
        // FUTURE: Title ALIVE (root via [Parameter] attribute -> ParameterAttribute alive via its
        // signature edge); only the ordinary Unbound property is flagged.
        AssertExactly(await FindingsIn("CatH.H6"), "CatH.H6.MyComponent.Unbound");
    }

    // H7 — CONFIRMED RED TODAY: [MainViewModel.Greeting, MainViewModel.Save(), MainViewModel.Unbound]
    // (Greeting/Save flagged; XAML binding is invisible).
    [Fact(Skip = "H7 — WS5: XAML plugin (WPF/MAUI {Binding} targets); mitigation today: ignore.namespaces [\"CatH.H7\"]")]
    [Trait("status", "moat")]
    public async Task H7_xaml_binding_targets_alive()
    {
        // FUTURE: Greeting and Save() ALIVE (bound from XAML markup); only Unbound is flagged.
        AssertExactly(await FindingsIn("CatH.H7"), "CatH.H7.MainViewModel.Unbound");
    }

    // H8 — CONFIRMED RED TODAY: [DefaultPage, OrphanPage] (DefaultPage flagged; .aspx markup is
    // invisible).
    [Fact(Skip = "H8 — WS5: WebForms plugin (.aspx/.ascx code-behind); mitigation today: ignore.files [\"**/*.aspx.cs\", \"**/*.ascx.cs\"]")]
    [Trait("status", "moat")]
    public async Task H8_webforms_codebehind_alive()
    {
        // FUTURE: DefaultPage ALIVE (reachable from its .aspx markup); OrphanPage (no markup) flagged.
        AssertExactly(await FindingsIn("CatH.H8"), "CatH.H8.OrphanPage");
    }

    // H9 — CONFIRMED RED TODAY: [CustomProvider, UnusedProvider] (CustomProvider flagged; the
    // config "type" string is invisible).
    [Fact(Skip = "H9 — WS5: config plugin (web.config/app.config type refs); mitigation today: ignore.symbols [\"CatH.H9.CustomProvider\"]")]
    [Trait("status", "moat")]
    public async Task H9_config_referenced_type_alive()
    {
        // FUTURE: CustomProvider ALIVE (named only in web.config); UnusedProvider flagged.
        AssertExactly(await FindingsIn("CatH.H9"), "CatH.H9.UnusedProvider");
    }

    // H10 — CONFIRMED RED TODAY: [Widget.Compute(), Widget.NeverInvoked()] (Compute flagged;
    // dynamic dispatch is undecidable, documented FP).
    [Fact(Skip = "H10 — WS5: dynamic dispatch is undecidable (designed FP); mitigation today: ignore.symbols [\"CatH.H10.Widget.Compute()\"]")]
    [Trait("status", "moat")]
    public async Task H10_dynamic_dispatch_member_alive()
    {
        // FUTURE (best-effort): Compute() ALIVE (invoked only via (dynamic)x); NeverInvoked() flagged.
        AssertExactly(await FindingsIn("CatH.H10"), "CatH.H10.Widget.NeverInvoked()");
    }

    // H11 — DECISION (no correct answer pinned). OBSERVED BEHAVIOR (2026-07): the sole caller of
    // Handler.Invoke() lives in H11.Generated.g.cs, which the default ignore.files ("**/*.g.cs")
    // skips WHOLESALE — DeadCodeAnalyzer `continue`s past the ignored tree, so its outbound edge is
    // never recorded and Invoke() is flagged dead alongside the genuinely-dead NeverReferenced().
    [Fact(Skip = "H11 — decision pending: walk generated trees for edges while never reporting their declarations")]
    [Trait("status", "decision")]
    public async Task H11_generated_code_edges_dropped()
    {
        // Pins TODAY's behavior (not a "correct" answer): both Invoke() (referenced only from the
        // ignored .g.cs) and NeverReferenced() are flagged.
        AssertExactly(await FindingsIn("CatH.H11"),
            "CatH.H11.Handler.Invoke()",
            "CatH.H11.Handler.NeverReferenced()");
    }

    // H12 — CONFIRMED RED TODAY: [IConsumer<TMessage>, OrderConsumer, OrderPlaced, UnrelatedService]
    // (OrderConsumer flagged; AddConsumers assembly scanning is invisible).
    [Fact(Skip = "H12 — WS5: MassTransit plugin (AddConsumer/scanning); mitigation today: entryPoints.implementedInterfaces [\"CatH.H12.IConsumer<T>\"] / name pattern")]
    [Trait("status", "moat")]
    public async Task H12_masstransit_consumer_alive()
    {
        // FUTURE: OrderConsumer ALIVE (root via consumer interface -> OrderPlaced & IConsumer alive);
        // only the non-consumer UnrelatedService is flagged.
        AssertExactly(await FindingsIn("CatH.H12"), "CatH.H12.UnrelatedService");
    }
}
