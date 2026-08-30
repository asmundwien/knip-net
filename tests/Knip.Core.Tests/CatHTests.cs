using Knip.Core.Analysis;
using Knip.Core.Configuration;
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
        FixtureRunner.FindingSymbolsInAsync(Category, ns, WithFrameworkAliases());

    private static KnipConfig WithFrameworkAliases(string? enabledPlugin = null)
    {
        var config = new KnipConfig();
        AddAliases(config, "scanningDi", new Dictionary<string, string[]>
        {
            ["MediatR.IRequestHandler"] = ["CatH.H4.IRequestHandler"],
            ["MassTransit.IConsumer"] = ["CatH.H12.IConsumer"],
        }, enabledPlugin == "scanningDi" ? true : null);
        AddAliases(config, "aspnetcore", new Dictionary<string, string[]>
        {
            ["Microsoft.AspNetCore.Builder.UseMiddlewareExtensions"] =
                ["CatH.AspNetMiddleware.ApplicationBuilder"],
            ["Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter"] =
                ["CatH.AspNetFilter.IAsyncActionFilter"],
        }, enabledPlugin == "aspnetcore" ? true : null);

        if (enabledPlugin == "serialization")
            AddAliases(config, "serialization", new Dictionary<string, string[]>
            {
                ["System.Text.Json.JsonSerializer"] = ["CatH.H5.JsonSerializer"],
            }, true);
        else if (enabledPlugin == "blazorParameter")
            AddAliases(config, "blazorParameter", new Dictionary<string, string[]>
            {
                ["Microsoft.AspNetCore.Components.ParameterAttribute"] = ["CatH.H6.ParameterAttribute"],
                ["Microsoft.AspNetCore.Components.CascadingParameterAttribute"] =
                    ["CatH.H6.CascadingParameterAttribute"],
                ["Microsoft.AspNetCore.Components.InjectAttribute"] = ["CatH.H6.InjectAttribute"],
            }, true);

        return config;
    }

    private static void AddAliases(
        KnipConfig config,
        string plugin,
        Dictionary<string, string[]> aliases,
        bool? enabled)
    {
        var settings = new PluginSettings { Enabled = enabled };
        settings.Extra[FrameworkTypeMatcher.AliasesSettingKey] =
            System.Text.Json.JsonSerializer.SerializeToElement(aliases);
        config.Plugins[plugin] = settings;
    }

    private static void AssertExactly(IReadOnlySet<string> actual, params string[] expectedDead) =>
        Assert.Equal(new HashSet<string>(expectedDead), actual);

    // H1 — PROMOTED (WS5 reflection plugin): GetMethod("Handle").Invoke keeps Handle() alive.
    [Fact]
    [Trait("status", "contract")]
    public async Task H1_reflection_invoked_member_alive()
    {
        // FUTURE: Handle() ALIVE (reached only via GetMethod("Handle").Invoke); NeverCalled() flagged.
        AssertExactly(await FindingsIn("CatH.H1"), "CatH.H1.Service.NeverCalled()");
    }

    // H2 — PROMOTED (WS5 reflection plugin): Type.GetType("CatH.H2.Plugin") keeps Plugin alive.
    [Fact]
    [Trait("status", "contract")]
    public async Task H2_string_named_type_alive()
    {
        // The external Plugin surface remains alive; the public member of the internal reflected type does not.
        AssertExactly(await FindingsIn("CatH.H2"),
            "CatH.H2.InternalPlugin.Run()",
            "CatH.H2.UnusedPlugin");
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

    // H4 — PROMOTED (WS5 scanningDi plugin): MyHandler implements IRequestHandler → scan-rooted → alive.
    [Fact]
    [Trait("status", "contract")]
    public async Task H4_assembly_scanned_handler_alive()
    {
        // MyHandler ALIVE (root via implemented IRequestHandler shape -> IRequestHandler alive too).
        // The internal handler's unrelated public sibling and the non-handler decoy remain flagged.
        AssertExactly(await FindingsIn("CatH.H4"),
            "CatH.H4.InternalHandler.UnusedPublicSibling()",
            "CatH.H4.UnrelatedType");
    }

    // H5 — PROMOTED (WS5 serialization plugin, opt-in): PersonDto is passed to Serialize -> the plugin
    // roots its public data members -> PersonDto.Name ALIVE. Runs WITH the plugin enabled.
    [Fact]
    [Trait("status", "contract")]
    public async Task H5_serialized_dto_property_alive()
    {
        // The serialization plugin is OFF by default; enable it explicitly for this contract.
        var config = WithFrameworkAliases("serialization");

        // PersonDto.Name ALIVE (root via the serialize call over PersonDto). OVER-ROOTING GUARD (two decoys):
        //   • NonDto.PlainDead — a plain member on a type that is NEVER serialized -> STAYS flagged (the
        //     plugin roots serialized types' members, not every property in the solution).
        //   • UnrelatedType — an unrelated dead type -> STAYS flagged (never rooted).
        AssertExactly(await FixtureRunner.FindingSymbolsInAsync(Category, "CatH.H5", config),
            "CatH.H5.NonDto.PlainDead",
            "CatH.H5.UnrelatedType");
    }

    // H6 — PROMOTED (WS5 blazorParameter plugin, opt-in): [Parameter]/[CascadingParameter]/[Inject] members
    // are set from .razor markup / DI -> the plugin roots them -> ALIVE. Runs WITH the plugin enabled.
    [Fact]
    [Trait("status", "contract")]
    public async Task H6_blazor_parameter_property_alive()
    {
        // The blazorParameter plugin is OFF by default; enable it explicitly for this contract.
        var config = WithFrameworkAliases("blazorParameter");

        // Title/Theme/Clock ALIVE (root via [Parameter]/[CascadingParameter]/[Inject]; each marker attribute
        // alive via its signature edge). OVER-ROOTING GUARD: only the attribute-less Unbound property and the
        // unrelated UnrelatedType stay flagged — the plugin roots ONLY attribute-bearing members.
        AssertExactly(await FixtureRunner.FindingSymbolsInAsync(Category, "CatH.H6", config),
            "CatH.H6.MyComponent.Unbound",
            "CatH.H6.UnrelatedType");
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

    // H11 — PROMOTED to CONTRACT (DECIDED 2026-07-15): built-in generated trees are WALKED for their
    // outbound edges/roots but their declarations are NEVER reported (extends the G8 compiler-generated
    // rule from symbols to files). H11.Generated.g.cs is detected as generated (both the "*.g.cs"
    // pattern and the "// <auto-generated/>" header), so:
    //   (a) Handler.Invoke() is ALIVE — its sole caller is the generated Register(); the walked edge
    //       confers liveness (was flagged dead under the old wholesale-drop behavior);
    //   (b) GeneratedWiring.Register()/RegisterDead(), though unreachable, are NEVER reported — they
    //       live in the generated tree;
    //   (c) the decoy Handler.NeverReferenced() (an ordinary dead USER method in a normal file) is
    //       STILL flagged — walking generated trees must not blanket-root user code.
    // BEFORE (decision row, skip-tagged): pinned TODAY's behavior — BOTH Invoke() and NeverReferenced()
    // flagged. AFTER (this contract): ONLY the decoy NeverReferenced() is flagged; Invoke() is alive and
    // the generated declarations are suppressed. This is the sanctioned promotion of the decided row.
    [Fact]
    [Trait("status", "contract")]
    public async Task H11_walks_generated_trees_for_edges_never_reports_their_declarations()
    {
        AssertExactly(await FindingsIn("CatH.H11"),
            "CatH.H11.Handler.NeverReferenced()");
    }

    // H12 — PROMOTED (WS5 scanningDi plugin): OrderConsumer implements IConsumer<> → scan-rooted → alive.
    [Fact]
    [Trait("status", "contract")]
    public async Task H12_masstransit_consumer_alive()
    {
        // OrderConsumer ALIVE (root via IConsumer<> shape -> OrderPlaced & IConsumer alive);
        // the non-consumer UnrelatedService is the over-rooting DECOY -> still flagged.
        AssertExactly(await FindingsIn("CatH.H12"), "CatH.H12.UnrelatedService");
    }

    // H13 — PROMOTED (WS5 aspnetcore plugin, opt-in): UseMiddleware<AuditLoggingMiddleware>() keeps the
    // TYPE alive but the framework calls Invoke reflectively -> Invoke + ctor + _next/_logger + the private
    // helper cascade dead. The plugin roots the convention entry members -> all ALIVE. Runs WITH plugin ON.
    [Fact]
    [Trait("status", "contract")]
    public async Task H13_middleware_invoke_and_helpers_alive()
    {
        // The aspnetcore plugin is OFF by default; enable it explicitly for this contract.
        var config = WithFrameworkAliases("aspnetcore");

        // Invoke, the ctor, _next/_logger and LeggTilRequestMetadata ALIVE (rooted convention entry +
        // liveness via edges — Invoke's real edges to _next/_logger/ILogger.Log keep those alive too).
        // OVER-ROOTING GUARD: the decoy NeverInvokedByPipeline() (Invoke never calls it) STAYS the only flag.
        AssertExactly(await FixtureRunner.FindingSymbolsInAsync(Category, "CatH.AspNetMiddleware", config),
            "CatH.AspNetMiddleware.AuditLoggingMiddleware.NeverInvokedByPipeline()");
    }

    // H14 — PROMOTED (WS5 aspnetcore plugin, opt-in): AuditFilter implements IAsyncActionFilter; the
    // framework dispatches OnActionExecutingAsync reflectively -> it + the private helper it calls cascade
    // dead. The plugin roots the filter's interface-method implementations -> ALIVE. Runs WITH plugin ON.
    [Fact]
    [Trait("status", "contract")]
    public async Task H14_filter_method_and_helper_alive()
    {
        var config = WithFrameworkAliases("aspnetcore");

        // OnActionExecutingAsync + LeggTilTjenestenavn ALIVE. OVER-ROOTING GUARD: the decoy NeverDispatched()
        // (the filter never calls it) STAYS flagged; the uncalled interface declaration also stays flagged.
        AssertExactly(await FixtureRunner.FindingSymbolsInAsync(Category, "CatH.AspNetFilter", config),
            "CatH.AspNetFilter.AuditFilter.NeverDispatched()",
            "CatH.AspNetFilter.IAsyncActionFilter.OnActionExecutingAsync(CatH.AspNetFilter.ActionExecutingContext, CatH.AspNetFilter.ActionExecutionDelegate)");
    }
}
