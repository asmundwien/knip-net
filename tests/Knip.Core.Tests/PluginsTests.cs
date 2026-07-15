using Knip.Core.Configuration;
using Knip.Core.Plugins;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// WS5 — the plugin seam and the built-in plugins (<c>reflection</c>, <c>scanningDi</c>). All rows are
/// Contract.
///
/// Covers three sign-off conditions:
///   • The per-plugin differential (flagged WITHOUT the plugin, alive WITH it) — the same fixtures the
///     CatH battery promotes (reflection→H1/H2, scanningDi→H4/H12) — PLUS an over-rooting guard: an
///     unrelated dead symbol that must STAY FLAGGED with the plugin ON (the plugin-OFF run alone is not
///     sufficient).
///   • The default-enabled set (F8-style): pins which plugins run under a default KnipConfig.
///   • Config warnings: an unknown plugin id and an unknown per-plugin key each surface a VISIBLE
///     diagnostic — they never silently no-op.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class PluginsTests
{
    private const string Category = "CatH";

    private static KnipConfig WithReflection(bool enabled) => new()
    {
        Plugins = { ["reflection"] = new PluginSettings { Enabled = enabled } },
    };

    private static KnipConfig WithScanningDi(bool enabled) => new()
    {
        Plugins = { ["scanningDi"] = new PluginSettings { Enabled = enabled } },
    };

    private static KnipConfig WithBlazorParameter(bool enabled) => new()
    {
        Plugins = { ["blazorParameter"] = new PluginSettings { Enabled = enabled } },
    };

    private static KnipConfig WithSerialization(bool enabled) => new()
    {
        Plugins = { ["serialization"] = new PluginSettings { Enabled = enabled } },
    };

    private static KnipConfig WithAspNetCore(bool enabled) => new()
    {
        Plugins = { ["aspnetcore"] = new PluginSettings { Enabled = enabled } },
    };

    private static Task<IReadOnlySet<string>> FindingsIn(string ns, KnipConfig config) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns, config);

    // ── reflection differential + over-rooting guard: H1 (GetMethod("X").Invoke) ──────────────
    [Fact]
    public async Task Reflection_getmethod_string_keeps_member_alive_H1()
    {
        // Plugin OFF: Handle() is a false positive (reached only via reflection) → flagged.
        var off = await FindingsIn("CatH.H1", WithReflection(false));
        Assert.Contains("CatH.H1.Service.Handle()", off);

        // Plugin ON: the plugin roots the reflected member → Handle() alive → NOT reported…
        var on = await FindingsIn("CatH.H1", WithReflection(true));
        Assert.DoesNotContain("CatH.H1.Service.Handle()", on);
        // …but the unrelated dead sibling STAYS FLAGGED (over-rooting guard: the plugin roots only the
        // reflected member on the reflected type, not every member of it).
        Assert.Contains("CatH.H1.Service.NeverCalled()", on);
    }

    // ── reflection differential + over-rooting guard: H2 (Type.GetType("Ns.Foo")) ─────────────
    [Fact]
    public async Task Reflection_typegettype_string_keeps_type_alive_H2()
    {
        // Plugin OFF: the string-named type is a false positive → flagged.
        var off = await FindingsIn("CatH.H2", WithReflection(false));
        Assert.Contains("CatH.H2.Plugin", off);

        // Plugin ON: Type.GetType("CatH.H2.Plugin") roots the named type → alive → NOT reported…
        var on = await FindingsIn("CatH.H2", WithReflection(true));
        Assert.DoesNotContain("CatH.H2.Plugin", on);
        // …but the unrelated dead type STAYS FLAGGED (plugin resolves EXACTLY the string it sees).
        Assert.Contains("CatH.H2.UnusedPlugin", on);
    }

    // ── scanningDi differential + over-rooting guard: H4 (MediatR IRequestHandler shape) ──────
    [Fact]
    public async Task ScanningDi_mediatr_handler_shape_keeps_type_alive_H4()
    {
        // Plugin OFF: MyHandler is registered only by assembly scanning → invisible → flagged.
        var off = await FindingsIn("CatH.H4", WithScanningDi(false));
        Assert.Contains("CatH.H4.MyHandler", off);

        // Plugin ON: the plugin roots types implementing IRequestHandler → MyHandler alive → NOT reported…
        var on = await FindingsIn("CatH.H4", WithScanningDi(true));
        Assert.DoesNotContain("CatH.H4.MyHandler", on);
        // …but the unrelated dead type STAYS FLAGGED (over-rooting guard: UnrelatedType implements no
        // scanned marker interface, so the shape test does not root it).
        Assert.Contains("CatH.H4.UnrelatedType", on);
    }

    // ── scanningDi differential + over-rooting guard: H12 (MassTransit IConsumer<> shape) ──────
    [Fact]
    public async Task ScanningDi_masstransit_consumer_shape_keeps_type_alive_H12()
    {
        // Plugin OFF: OrderConsumer is registered only by AddConsumers scanning → invisible → flagged.
        var off = await FindingsIn("CatH.H12", WithScanningDi(false));
        Assert.Contains("CatH.H12.OrderConsumer", off);

        // Plugin ON: the plugin roots types implementing IConsumer<> → OrderConsumer alive → NOT reported…
        var on = await FindingsIn("CatH.H12", WithScanningDi(true));
        Assert.DoesNotContain("CatH.H12.OrderConsumer", on);
        // …but the non-consumer STAYS FLAGGED (over-rooting guard: implements no scanned marker).
        Assert.Contains("CatH.H12.UnrelatedService", on);
    }

    // ── blazorParameter differential + over-rooting guard: H6 ([Parameter]/[CascadingParameter]/[Inject]) ──
    [Fact]
    public async Task BlazorParameter_attribute_members_kept_alive_H6()
    {
        // Plugin OFF (also the DEFAULT — it is opt-in): the markup-/DI-set members are false positives → flagged.
        var off = await FindingsIn("CatH.H6", WithBlazorParameter(false));
        Assert.Contains("CatH.H6.MyComponent.Title", off);   // [Parameter]
        Assert.Contains("CatH.H6.MyComponent.Theme", off);   // [CascadingParameter]
        Assert.Contains("CatH.H6.MyComponent.Clock", off);   // [Inject]

        // Plugin ON: the plugin roots the attribute-bearing members → alive → NOT reported…
        var on = await FindingsIn("CatH.H6", WithBlazorParameter(true));
        Assert.DoesNotContain("CatH.H6.MyComponent.Title", on);
        Assert.DoesNotContain("CatH.H6.MyComponent.Theme", on);
        Assert.DoesNotContain("CatH.H6.MyComponent.Clock", on);
        // …but the attribute-less sibling and the unrelated type STAY FLAGGED (over-rooting guard: the
        // plugin roots ONLY attribute-bearing members, never the whole component or its neighbours).
        Assert.Contains("CatH.H6.MyComponent.Unbound", on);
        Assert.Contains("CatH.H6.UnrelatedType", on);
    }

    // blazorParameter is OFF by default (opt-in): a default config leaves the [Parameter] member flagged.
    [Fact]
    public async Task BlazorParameter_is_off_by_default()
    {
        Assert.DoesNotContain("blazorParameter", PluginRegistry.DefaultEnabledIds);
        Assert.False(new KnipConfig().IsPluginEnabled(Descriptor("blazorParameter")),
            "blazorParameter must default OFF (opt-in)");

        var byDefault = await FindingsIn("CatH.H6", new KnipConfig());
        Assert.Contains("CatH.H6.MyComponent.Title", byDefault);
    }

    // ── serialization differential + over-rooting guard: H5 (Serialize(dto) over DTO props) ────────
    [Fact]
    public async Task Serialization_serialized_dto_members_kept_alive_H5()
    {
        // Plugin OFF (also the DEFAULT — it is opt-in): the serializer-touched property is a false
        // positive (no source reads dto.Name; the serializer reflects over it) → flagged.
        var off = await FindingsIn("CatH.H5", WithSerialization(false));
        Assert.Contains("CatH.H5.PersonDto.Name", off);

        // Plugin ON: PersonDto is passed to Serialize → the plugin roots its public data members →
        // Name alive → NOT reported…
        var on = await FindingsIn("CatH.H5", WithSerialization(true));
        Assert.DoesNotContain("CatH.H5.PersonDto.Name", on);
        // …but two decoys STAY FLAGGED (over-rooting guard: the plugin roots only serialized types' own
        // data members, never every property in the solution):
        Assert.Contains("CatH.H5.NonDto.PlainDead", on);  // plain member on a NON-serialized type
        Assert.Contains("CatH.H5.UnrelatedType", on);     // unrelated dead type
    }

    // serialization is OFF by default (opt-in): a default config leaves the serialized property flagged.
    [Fact]
    public async Task Serialization_is_off_by_default()
    {
        Assert.DoesNotContain("serialization", PluginRegistry.DefaultEnabledIds);
        Assert.False(new KnipConfig().IsPluginEnabled(Descriptor("serialization")),
            "serialization must default OFF (opt-in)");

        var byDefault = await FindingsIn("CatH.H5", new KnipConfig());
        Assert.Contains("CatH.H5.PersonDto.Name", byDefault);
    }

    // The optional 'namespaces' glob roots the data members of matching-namespace types even when the
    // plugin can't see them serialized — and is a RECOGNIZED key (no unknown-key warning).
    [Fact]
    public async Task Serialization_namespaces_glob_roots_dto_members_by_namespace()
    {
        var settings = new PluginSettings { Enabled = true };
        settings.Extra["namespaces"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { "CatH.H5" });
        var config = new KnipConfig { Plugins = { ["serialization"] = settings } };

        // With CatH.H5 declared a DTO namespace, NonDto.PlainDead (a plain member the serialize-call rule
        // would NOT root) is now rooted by the namespace glob → alive → NOT reported…
        var on = await FindingsIn("CatH.H5", config);
        Assert.DoesNotContain("CatH.H5.NonDto.PlainDead", on);
        Assert.DoesNotContain("CatH.H5.PersonDto.Name", on);
        // …while the unrelated dead TYPE stays flagged (namespace rooting is data-members-only).
        Assert.Contains("CatH.H5.UnrelatedType", on);

        // 'namespaces' is a known key → no unknown-key warning.
        var result = await FixtureRunner.RunAsync(Category, config);
        Assert.DoesNotContain(result.LoadDiagnostics, d => d.Contains("plugins.serialization.namespaces"));
    }

    // ── aspnetcore differential + over-rooting guard: middleware Invoke (UseMiddleware<T>) ──────────
    [Fact]
    public async Task AspNetCore_middleware_invoke_and_cascade_kept_alive()
    {
        const string ns = "CatH.AspNetMiddleware";
        // Plugin OFF (also the DEFAULT — it is opt-in): the framework calls Invoke reflectively, so Invoke +
        // its ctor + fields + private helper are all false positives cascading dead → flagged.
        var off = await FindingsIn(ns, WithAspNetCore(false));
        Assert.Contains(ns + ".AuditLoggingMiddleware.Invoke(CatH.AspNetMiddleware.HttpContext)", off);
        Assert.Contains(ns + ".AuditLoggingMiddleware.LeggTilRequestMetadata(CatH.AspNetMiddleware.HttpContext)", off);
        Assert.Contains(ns + ".AuditLoggingMiddleware._next", off);
        Assert.Contains(ns + ".AuditLoggingMiddleware._logger", off);

        // Plugin ON: the plugin roots the convention entry (Invoke + ctors) → Invoke, ctor, _next/_logger and
        // the private helper are alive → NOT reported…
        var on = await FindingsIn(ns, WithAspNetCore(true));
        Assert.DoesNotContain(ns + ".AuditLoggingMiddleware.Invoke(CatH.AspNetMiddleware.HttpContext)", on);
        Assert.DoesNotContain(ns + ".AuditLoggingMiddleware.LeggTilRequestMetadata(CatH.AspNetMiddleware.HttpContext)", on);
        Assert.DoesNotContain(ns + ".AuditLoggingMiddleware._next", on);
        Assert.DoesNotContain(ns + ".AuditLoggingMiddleware._logger", on);
        // …but the unrelated dead method STAYS FLAGGED (over-rooting guard: the plugin roots ONLY the
        // convention entry members, never a method Invoke doesn't call).
        Assert.Contains(ns + ".AuditLoggingMiddleware.NeverInvokedByPipeline()", on);
    }

    // ── aspnetcore differential + over-rooting guard: MVC filter interface method ───────────────────
    [Fact]
    public async Task AspNetCore_filter_method_and_helper_kept_alive()
    {
        const string ns = "CatH.AspNetFilter";
        const string helper = ns + ".AuditFilter.LeggTilTjenestenavn(CatH.AspNetFilter.ActionExecutingContext)";

        // Plugin OFF (also the DEFAULT): the framework dispatches OnActionExecutingAsync reflectively, so the
        // concrete impl gains no incoming edge — the private helper it calls CASCADES to a false positive →
        // flagged. (The impl itself is suppressed by the interface-implementation rule; the visible cascade is
        // the helper, which is exactly the FP class this plugin kills.)
        var off = await FindingsIn(ns, WithAspNetCore(false));
        Assert.Contains(helper, off);

        // Plugin ON: the plugin roots the filter's implementation of the interface methods → the helper it
        // calls gains liveness → NOT reported…
        var on = await FindingsIn(ns, WithAspNetCore(true));
        Assert.DoesNotContain(helper, on);
        // …but the unrelated dead method STAYS FLAGGED (over-rooting guard: only the interface-method
        // implementations are rooted, not the whole type).
        Assert.Contains(ns + ".AuditFilter.NeverDispatched()", on);
    }

    // aspnetcore is OFF by default (opt-in): a default config leaves the middleware Invoke flagged.
    [Fact]
    public async Task AspNetCore_is_off_by_default()
    {
        Assert.DoesNotContain("aspnetcore", PluginRegistry.DefaultEnabledIds);
        Assert.False(new KnipConfig().IsPluginEnabled(Descriptor("aspnetcore")),
            "aspnetcore must default OFF (opt-in)");

        var byDefault = await FindingsIn("CatH.AspNetMiddleware", new KnipConfig());
        Assert.Contains("CatH.AspNetMiddleware.AuditLoggingMiddleware.Invoke(CatH.AspNetMiddleware.HttpContext)", byDefault);
    }

    // A typo in the aspnetcore block surfaces a visible unknown-key warning (never silently no-ops).
    [Fact]
    public async Task UnknownAspNetCoreKey_emits_visible_warning()
    {
        var settings = new PluginSettings { Enabled = true };
        settings.Extra["enabldd"] = System.Text.Json.JsonSerializer.SerializeToElement(true);
        var config = new KnipConfig { Plugins = { ["aspnetcore"] = settings } };

        var result = await FixtureRunner.RunAsync(Category, config);
        Assert.Contains(result.LoadDiagnostics, d => d.Contains("plugins.aspnetcore.enabldd"));
    }

    // A typo in the blazorParameter block surfaces a visible unknown-key warning (never silently no-ops).
    [Fact]
    public async Task UnknownBlazorParameterKey_emits_visible_warning()
    {
        var settings = new PluginSettings { Enabled = true };
        settings.Extra["enabldd"] = System.Text.Json.JsonSerializer.SerializeToElement(true);
        var config = new KnipConfig { Plugins = { ["blazorParameter"] = settings } };

        var result = await FixtureRunner.RunAsync(Category, config);
        Assert.Contains(result.LoadDiagnostics, d => d.Contains("plugins.blazorParameter.enabldd"));
    }

    // ── default-enabled set (F8-style): pin which plugins run under a default config ──────────
    [Fact]
    public void DefaultEnabledSet_is_reflection_and_scanningDi_for_v1()
    {
        // The shipped default-on set: reflection + scanningDi (approved). This pins the seam's default so
        // a regression (accidentally enabling/disabling a plugin) is caught.
        Assert.Equal(new[] { "reflection", "scanningDi" }, PluginRegistry.DefaultEnabledIds);

        var config = new KnipConfig();
        Assert.True(config.IsPluginEnabled(Descriptor("reflection")), "reflection must default ON");
        Assert.True(config.IsPluginEnabled(Descriptor("scanningDi")), "scanningDi must default ON");
    }

    [Fact]
    public async Task DefaultConfig_runs_reflection_so_H2_type_is_alive()
    {
        // RED-FLIP through config, analogous to F8: default config (reflection ON by default) keeps the
        // reflected type alive; explicitly disabling reflection flips it back to flagged.
        var byDefault = await FindingsIn("CatH.H2", new KnipConfig());
        Assert.DoesNotContain("CatH.H2.Plugin", byDefault);

        var disabled = await FindingsIn("CatH.H2", WithReflection(false));
        Assert.Contains("CatH.H2.Plugin", disabled);
    }

    // ── config warnings: unknown plugin id ────────────────────────────────────────────────────
    [Fact]
    public async Task UnknownPluginId_emits_visible_warning()
    {
        var config = new KnipConfig
        {
            Plugins = { ["reflectoin"] = new PluginSettings { Enabled = true } }, // typo of "reflection"
        };
        var result = await FixtureRunner.RunAsync(Category, config);
        Assert.Contains(result.LoadDiagnostics, d => d.Contains("unknown plugin 'reflectoin'"));
    }

    // ── config warnings: unknown per-plugin key ───────────────────────────────────────────────
    [Fact]
    public async Task UnknownPerPluginKey_emits_visible_warning()
    {
        // A recognized plugin id, but a typo'd setting key must not silently no-op.
        var settings = new PluginSettings { Enabled = true };
        settings.Extra["enabldd"] = System.Text.Json.JsonSerializer.SerializeToElement(true);
        var config = new KnipConfig { Plugins = { ["reflection"] = settings } };

        var result = await FixtureRunner.RunAsync(Category, config);
        Assert.Contains(result.LoadDiagnostics, d => d.Contains("plugins.reflection.enabldd"));
    }

    // ── config warnings: unknown per-plugin key on scanningDi ──────────────────────────────────
    [Fact]
    public async Task UnknownScanningDiKey_emits_visible_warning()
    {
        var settings = new PluginSettings { Enabled = true };
        settings.Extra["enabldd"] = System.Text.Json.JsonSerializer.SerializeToElement(true);
        var config = new KnipConfig { Plugins = { ["scanningDi"] = settings } };

        var result = await FixtureRunner.RunAsync(Category, config);
        Assert.Contains(result.LoadDiagnostics, d => d.Contains("plugins.scanningDi.enabldd"));
    }

    // A clean, fully-specified config produces NO plugin warnings (anti-vacuous-green for the above).
    [Fact]
    public void ValidatePlugins_clean_config_has_no_warnings()
    {
        var config = new KnipConfig
        {
            Plugins = { ["reflection"] = new PluginSettings { Enabled = true } },
        };
        Assert.Empty(config.ValidatePlugins());
    }

    private static PluginDescriptor Descriptor(string id) =>
        PluginRegistry.All.Single(d => d.Id == id);
}
