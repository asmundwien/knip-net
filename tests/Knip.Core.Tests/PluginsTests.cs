using Knip.Core.Analysis;
using Knip.Core.Configuration;
using Knip.Core.Plugins;
using Knip.Core.Model;
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

    private static KnipConfig WithScanningDi(bool enabled) => WithAliases(
        "scanningDi",
        enabled,
        new Dictionary<string, string[]>
        {
            ["MediatR.IRequestHandler"] = ["CatH.H4.IRequestHandler"],
            ["MassTransit.IConsumer"] = ["CatH.H12.IConsumer"],
            ["Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions"] =
                ["CatH.DiConstructorActivation.ServiceCollectionServiceExtensions"],
            ["Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions"] =
                ["CatH.DiConstructorActivation.ServiceCollectionDescriptorExtensions"],
        });

    private static KnipConfig WithBlazorParameter(bool enabled) => WithAliases(
        "blazorParameter",
        enabled,
        new Dictionary<string, string[]>
        {
            ["Microsoft.AspNetCore.Components.ParameterAttribute"] = ["CatH.H6.ParameterAttribute"],
            ["Microsoft.AspNetCore.Components.CascadingParameterAttribute"] =
                ["CatH.H6.CascadingParameterAttribute"],
            ["Microsoft.AspNetCore.Components.InjectAttribute"] = ["CatH.H6.InjectAttribute"],
        });

    private static KnipConfig WithSerialization(bool enabled) => WithAliases(
        "serialization",
        enabled,
        new Dictionary<string, string[]>
        {
            ["System.Text.Json.JsonSerializer"] = ["CatH.H5.JsonSerializer", "CatH.H20.JsonSerializer"],
        });

    private static KnipConfig WithAspNetCore(bool enabled) => WithAliases(
        "aspnetcore",
        enabled,
        new Dictionary<string, string[]>
        {
            ["Microsoft.AspNetCore.Builder.UseMiddlewareExtensions"] =
                ["CatH.AspNetMiddleware.ApplicationBuilder"],
            ["Microsoft.AspNetCore.Http.IMiddleware"] = ["CatH.AspNetFrameworkActivation.IMiddleware"],
            ["Microsoft.AspNetCore.Hosting.IStartupFilter"] =
                ["CatH.AspNetFrameworkActivation.IStartupFilter"],
            ["Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter"] =
                ["CatH.AspNetFilter.IAsyncActionFilter"],
            ["Microsoft.AspNetCore.Authorization.AuthorizationHandler"] =
                ["CatH.AspNetAuthHandler.AuthorizationHandler"],
            ["Microsoft.AspNetCore.Components.ComponentBase"] = ["CatH.BlazorLifecycle.ComponentBase"],
            ["Microsoft.ApplicationInsights.Extensibility.ITelemetryProcessor"] =
                ["CatH.AspNetTelemetry.ITelemetryProcessor"],
            ["Microsoft.ApplicationInsights.Extensibility.ITelemetryInitializer"] =
                ["CatH.AspNetTelemetry.ITelemetryInitializer"],
            ["Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck"] =
                ["CatH.AspNetHealthCheck.IHealthCheck"],
            ["Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider"] =
                ["CatH.AspNetPolicyProvider.IAuthorizationPolicyProvider"],
            ["Microsoft.AspNetCore.Authorization.DefaultAuthorizationPolicyProvider"] =
                ["CatH.AspNetPolicyProvider.DefaultAuthorizationPolicyProvider"],
        });

    private static KnipConfig WithAliases(
        string plugin,
        bool enabled,
        Dictionary<string, string[]> aliases)
    {
        var settings = new PluginSettings { Enabled = enabled };
        settings.Extra[FrameworkTypeMatcher.AliasesSettingKey] =
            System.Text.Json.JsonSerializer.SerializeToElement(aliases);
        return new KnipConfig { Plugins = { [plugin] = settings } };
    }

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
        Assert.DoesNotContain("CatH.H2.Plugin.Run()", on);
        Assert.Contains("CatH.H2.InternalPlugin.Run()", on);
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
        Assert.Contains("CatH.H4.InternalHandler.UnusedPublicSibling()", on);
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

    [Fact]
    public async Task ScanningDi_registration_roots_implementation_constructor_closure()
    {
        const string ns = "CatH.DiConstructorActivation";

        var findings = await FindingsIn(ns, WithScanningDi(true));

        Assert.DoesNotContain(ns + ".RegisteredService._state", findings);
        Assert.DoesNotContain(ns + ".RegisteredService.BuildState()", findings);
        Assert.DoesNotContain(ns + ".RegisteredService._initializedState", findings);
        Assert.DoesNotContain(ns + ".RegisteredService.BuildInitializedState()", findings);
        Assert.Contains(ns + ".RegisteredService.NeverCalled()", findings);
    }

    [Fact]
    public async Task ScanningDi_type_registration_roots_implementation_constructor_closure()
    {
        const string ns = "CatH.DiConstructorActivation";

        var findings = await FindingsIn(ns, WithScanningDi(true));

        Assert.DoesNotContain(ns + ".TypeRegisteredService._state", findings);
        Assert.DoesNotContain(ns + ".TypeRegisteredService.BuildState()", findings);
        Assert.Contains(ns + ".TypeRegisteredService.NeverCalled()", findings);
    }

    [Fact]
    public async Task ScanningDi_single_type_registration_roots_implicit_activation_closure()
    {
        const string ns = "CatH.DiConstructorActivation";

        var findings = await FindingsIn(ns, WithScanningDi(true));

        Assert.DoesNotContain(ns + ".SingleTypeRegisteredService._state", findings);
        Assert.DoesNotContain(ns + ".SingleTypeRegisteredService.BuildState()", findings);
        Assert.Contains(ns + ".SingleTypeRegisteredService.NeverCalled()", findings);
    }

    [Fact]
    public async Task ScanningDi_try_add_single_type_registration_roots_activation_closure()
    {
        const string ns = "CatH.DiConstructorActivation";

        var findings = await FindingsIn(ns, WithScanningDi(true));

        Assert.DoesNotContain(ns + ".TryAddRegisteredService._state", findings);
        Assert.DoesNotContain(ns + ".TryAddRegisteredService.BuildState()", findings);
        Assert.Contains(ns + ".TryAddRegisteredService.NeverCalled()", findings);
    }

    [Fact]
    public async Task ScanningDi_metadata_registration_roots_source_initializer_closure()
    {
        const string ns = "CatH.DiConstructorActivation.Metadata";

        var findings = await FindingsIn(ns, WithScanningDi(true));

        Assert.DoesNotContain(ns + ".MetadataRegisteredService._state", findings);
        Assert.DoesNotContain(ns + ".MetadataRegisteredService.BuildState()", findings);
        Assert.Contains(ns + ".MetadataRegisteredService.NeverCalled()", findings);
    }

    [Fact]
    public async Task ScanningDi_static_extension_invocation_roots_activation_closure()
    {
        const string ns = "CatH.DiConstructorActivation";

        var findings = await FindingsIn(ns, WithScanningDi(true));

        Assert.DoesNotContain(ns + ".StaticRegisteredService._state", findings);
        Assert.DoesNotContain(ns + ".StaticRegisteredService.BuildState()", findings);
        Assert.Contains(ns + ".StaticRegisteredService.NeverCalled()", findings);
    }

    [Fact]
    public async Task ScanningDi_activation_preserves_base_constructor_and_initializer_closures()
    {
        const string ns = "CatH.DiConstructorActivation";

        var findings = await FindingsIn(ns, WithScanningDi(true));

        Assert.DoesNotContain(ns + ".RegisteredServiceBase._constructedState", findings);
        Assert.DoesNotContain(ns + ".RegisteredServiceBase.BuildConstructedState()", findings);
        Assert.DoesNotContain(ns + ".RegisteredServiceBase._initializedState", findings);
        Assert.DoesNotContain(ns + ".RegisteredServiceBase.BuildInitializedState()", findings);
        Assert.Contains(ns + ".RegisteredServiceBase.NeverCalled()", findings);
    }

    [Fact]
    public async Task ScanningDi_uncertain_factory_activation_marks_activation_closure_as_hazardous()
    {
        const string ns = "CatH.DiConstructorActivation";
        var result = await FixtureRunner.RunAsync(Category, WithScanningDi(false));

        var field = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".FactoryRegisteredService._state");
        Assert.Contains(Hazard.DiPluginShaped, field.Hazards);
        Assert.Equal(Confidence.Low, field.Confidence);

        var helper = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".FactoryRegisteredService.BuildState()");
        Assert.Contains(Hazard.DiPluginShaped, helper.Hazards);
        Assert.Equal(Confidence.Low, helper.Confidence);

        var unrelated = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".FactoryRegisteredService.NeverCalled()");
        Assert.DoesNotContain(Hazard.DiPluginShaped, unrelated.Hazards);
        Assert.Equal(Confidence.High, unrelated.Confidence);
    }

    [Fact]
    public async Task ScanningDi_try_add_type_factory_marks_only_activation_closure_as_hazardous()
    {
        const string ns = "CatH.DiConstructorActivation";
        var result = await FixtureRunner.RunAsync(Category, WithScanningDi(false));

        var field = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".TryAddFactoryRegisteredService._state");
        Assert.Contains(Hazard.DiPluginShaped, field.Hazards);
        Assert.Equal(Confidence.Low, field.Confidence);

        var helper = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".TryAddFactoryRegisteredService.BuildState()");
        Assert.Contains(Hazard.DiPluginShaped, helper.Hazards);
        Assert.Equal(Confidence.Low, helper.Confidence);

        var unrelated = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".TryAddFactoryRegisteredService.NeverCalled()");
        Assert.DoesNotContain(Hazard.DiPluginShaped, unrelated.Hazards);
        Assert.Equal(Confidence.High, unrelated.Confidence);
    }

    [Fact]
    public async Task ScanningDi_metadata_type_factory_marks_source_initializer_closure_as_hazardous()
    {
        const string ns = "CatH.DiConstructorActivation.Metadata";
        var result = await FixtureRunner.RunAsync(Category, WithScanningDi(false));

        var field = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".MetadataFactoryRegisteredService._state");
        Assert.Contains(Hazard.DiPluginShaped, field.Hazards);
        Assert.Equal(Confidence.Low, field.Confidence);

        var helper = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".MetadataFactoryRegisteredService.BuildState()");
        Assert.Contains(Hazard.DiPluginShaped, helper.Hazards);
        Assert.Equal(Confidence.Low, helper.Confidence);

        var unrelated = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".MetadataFactoryRegisteredService.NeverCalled()");
        Assert.DoesNotContain(Hazard.DiPluginShaped, unrelated.Hazards);
        Assert.Equal(Confidence.High, unrelated.Confidence);
    }

    [Fact]
    public async Task ScanningDi_uncertain_instance_activation_marks_constructor_closure_as_hazardous()
    {
        const string ns = "CatH.DiConstructorActivation";
        var result = await FixtureRunner.RunAsync(Category, WithScanningDi(false));

        var field = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".InstanceRegisteredService._state");
        Assert.Contains(Hazard.DiPluginShaped, field.Hazards);
        Assert.Equal(Confidence.Low, field.Confidence);

        var helper = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".InstanceRegisteredService.BuildState()");
        Assert.Contains(Hazard.DiPluginShaped, helper.Hazards);
        Assert.Equal(Confidence.Low, helper.Confidence);

        var unrelated = Assert.Single(result.Findings, finding => finding.Symbol == ns + ".InstanceRegisteredService.NeverCalled()");
        Assert.DoesNotContain(Hazard.DiPluginShaped, unrelated.Hazards);
        Assert.Equal(Confidence.High, unrelated.Confidence);
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
    [Fact]
    public async Task Serialization_collection_element_members_are_hazard_shaped_H20()
    {
        var result = await FixtureRunner.RunAsync(Category, WithSerialization(false));
        var collectionTarget = Assert.Single(
            result.Findings,
            finding => finding.Symbol == "CatH.H20.SerializedItems.BatchName");
        Assert.Contains(Hazard.SerializationShaped, collectionTarget.Hazards);


        var element = Assert.Single(
            result.Findings,
            finding => finding.Symbol == "CatH.H20.SerializedItem.Value");
        Assert.Contains(Hazard.SerializationShaped, element.Hazards);
        Assert.Equal(Confidence.Low, element.Confidence);

        var arrayElement = Assert.Single(
            result.Findings,
            finding => finding.Symbol == "CatH.H20.ArrayItem.Value");
        Assert.Contains(Hazard.SerializationShaped, arrayElement.Hazards);

        var unrelated = Assert.Single(
            result.Findings,
            finding => finding.Symbol == "CatH.H20.UnrelatedCollaborator.DeadValue");
        Assert.DoesNotContain(Hazard.SerializationShaped, unrelated.Hazards);
    }
    [Fact]
    public async Task Serialization_roots_collection_element_members_only_H20()
    {
        var off = await FindingsIn("CatH.H20", WithSerialization(false));
        Assert.Contains("CatH.H20.SerializedItem.Value", off);
        Assert.Contains("CatH.H20.ArrayItem.Value", off);

        var on = await FindingsIn("CatH.H20", WithSerialization(true));
        Assert.DoesNotContain("CatH.H20.SerializedItem.Value", on);
        Assert.DoesNotContain("CatH.H20.ArrayItem.Value", on);
        Assert.Contains("CatH.H20.SerializedItem.Describe()", on);
        Assert.Contains("CatH.H20.UnrelatedCollaborator.DeadValue", on);
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
        Assert.Contains(ns + ".AuditFilter._state", off);
        Assert.Contains(ns + ".AuditFilter.BuildState()", off);

        // Plugin ON: the plugin roots the filter's implementation of the interface methods → the helper it
        // calls gains liveness → NOT reported…
        var on = await FindingsIn(ns, WithAspNetCore(true));
        Assert.DoesNotContain(helper, on);
        Assert.DoesNotContain(ns + ".AuditFilter._state", on);
        Assert.DoesNotContain(ns + ".AuditFilter.BuildState()", on);
        // …but the unrelated dead method STAYS FLAGGED (over-rooting guard: only the interface-method
        // implementations are rooted, not the whole type).
        Assert.Contains(ns + ".AuditFilter.NeverDispatched()", on);
    }

    [Fact]
    public async Task AspNetCore_factory_middleware_and_startup_filter_constructor_closures_stay_alive()
    {
        const string ns = "CatH.AspNetFrameworkActivation";

        var off = await FixtureRunner.FindingSymbolsInAsync(
            Category, ns, WithAspNetCore(false), includeSyntheticGlobalRoots: false);
        Assert.Contains(ns + ".FactoryMiddleware._state", off);
        Assert.Contains(ns + ".FactoryMiddleware.BuildState()", off);
        Assert.Contains(ns + ".RequestPipelineFilter._state", off);
        Assert.Contains(ns + ".RequestPipelineFilter.BuildState()", off);

        var on = await FixtureRunner.FindingSymbolsInAsync(
            Category, ns, WithAspNetCore(true), includeSyntheticGlobalRoots: false);
        Assert.DoesNotContain(ns + ".FactoryMiddleware._state", on);
        Assert.DoesNotContain(ns + ".FactoryMiddleware.BuildState()", on);
        Assert.DoesNotContain(ns + ".RequestPipelineFilter._state", on);
        Assert.DoesNotContain(ns + ".RequestPipelineFilter.BuildState()", on);
        Assert.Contains(ns + ".RequestPipelineFilter.NeverConfigured()", on);
    }

    // ── aspnetcore differential + over-rooting guard: authorization handler entry method (H15) ──────
    [Fact]
    public async Task AspNetCore_authorization_handler_entry_and_cascade_kept_alive_H15()
    {
        const string ns = "CatH.AspNetAuthHandler";
        const string helper = ns + ".ADGroupsHandler.SjekkTilgang(CatH.AspNetAuthHandler.AuthorizationHandlerContext)";

        // Plugin OFF (also the DEFAULT — it is opt-in): policy evaluation dispatches HandleRequirementAsync
        // reflectively, so the handler's ctor gains no incoming edge — its fields (_logger,
        // _authenticationStateProvider) and the private helper it calls all CASCADE to false positives.
        // (The override itself is suppressed by the override-implementation rule; the visible cascade is the
        // fields + helper — exactly the FP class this plugin kills, dogfound as ADGroupsHandler on Blåresept.)
        var off = await FindingsIn(ns, WithAspNetCore(false));
        Assert.Contains(helper, off);
        Assert.Contains(ns + ".ADGroupsHandler._logger", off);
        Assert.Contains(ns + ".ADGroupsHandler._authenticationStateProvider", off);

        // Plugin ON: the plugin roots the handler's entry method (HandleRequirementAsync) + ctors → its
        // fields and the private helper it calls gain liveness → NOT reported…
        var on = await FindingsIn(ns, WithAspNetCore(true));
        Assert.DoesNotContain(helper, on);
        Assert.DoesNotContain(ns + ".ADGroupsHandler._logger", on);
        Assert.DoesNotContain(ns + ".ADGroupsHandler._authenticationStateProvider", on);
        // …but the unrelated dead method STAYS FLAGGED (over-rooting guard: only the convention entry members
        // are rooted, never a method the handler doesn't call).
        Assert.Contains(ns + ".ADGroupsHandler.NeverEvaluated()", on);
    }

    // ── aspnetcore differential + over-rooting guard: Blazor component lifecycle method (H16) ────────
    [Fact]
    public async Task AspNetCore_blazor_lifecycle_method_and_helper_kept_alive_H16()
    {
        const string ns = "CatH.BlazorLifecycle";
        const string helper = ns + ".MyPage.LastInnData()";

        // Plugin OFF (also the DEFAULT): the Blazor renderer invokes OnInitialized by convention, so the
        // private helper it calls CASCADES to a false positive → flagged.
        var off = await FindingsIn(ns, WithAspNetCore(false));
        Assert.Contains(helper, off);
        Assert.Contains(ns + ".MyPage._state", off);
        Assert.Contains(ns + ".MyPage.BuildState()", off);

        // Plugin ON: the plugin roots the ComponentBase lifecycle methods → the helper OnInitialized calls
        // gains liveness → NOT reported…
        var on = await FindingsIn(ns, WithAspNetCore(true));
        Assert.DoesNotContain(helper, on);
        Assert.DoesNotContain(ns + ".MyPage._state", on);
        Assert.DoesNotContain(ns + ".MyPage.BuildState()", on);
        // …but the unrelated dead method STAYS FLAGGED (over-rooting guard: only the lifecycle methods are
        // rooted, not the whole component).
        Assert.Contains(ns + ".MyPage.NeverRendered()", on);
    }

    // ── aspnetcore differential + over-rooting guard: App-Insights telemetry (H17) ──────────────────
    [Fact]
    public async Task AspNetCore_telemetry_processor_and_initializer_kept_alive_H17()
    {
        const string ns = "CatH.AspNetTelemetry";
        const string procHelper = ns + ".SensitivDataTelemetryProcessor.FjernSensitivData(CatH.AspNetTelemetry.ITelemetry)";
        const string initHelper = ns + ".BrukerTelemetryInitializer.SettBrukerkontekst(CatH.AspNetTelemetry.ITelemetry)";

        // Plugin OFF (also the DEFAULT — it is opt-in): the telemetry pipeline dispatches Process/Initialize
        // reflectively, so the processor's ctor gains no incoming edge — the ctor-assigned _next and the
        // private helpers Process/Initialize call all CASCADE to false positives. (The Process/Initialize
        // overrides themselves are interface impls; the visible cascade is the field + helpers — exactly the
        // FP class this plugin kills, dogfound across Hdir.Hint.Logging.ApplicationInsights.)
        var off = await FindingsIn(ns, WithAspNetCore(false));
        Assert.Contains(procHelper, off);
        Assert.Contains(ns + ".SensitivDataTelemetryProcessor._next", off);
        Assert.Contains(initHelper, off);

        // Plugin ON: the plugin roots the entry methods (Process/Initialize) + instance ctors → the _next
        // field and the private helpers they call gain liveness → NOT reported…
        var on = await FindingsIn(ns, WithAspNetCore(true));
        Assert.DoesNotContain(procHelper, on);
        Assert.DoesNotContain(ns + ".SensitivDataTelemetryProcessor._next", on);
        Assert.DoesNotContain(initHelper, on);
        // …but the unrelated dead method STAYS FLAGGED (over-rooting guard: only the convention entry members
        // are rooted, never a method the processor doesn't call).
        Assert.Contains(ns + ".SensitivDataTelemetryProcessor.NeverProcessed()", on);
    }

    // ── aspnetcore differential + over-rooting guard: health check (H18) ─────────────────────────────
    [Fact]
    public async Task AspNetCore_health_check_entry_and_cascade_kept_alive_H18()
    {
        const string ns = "CatH.AspNetHealthCheck";
        const string helper = ns + ".ConfigurationHealthCheck.LesTerskel()";

        // Plugin OFF (also the DEFAULT — it is opt-in): the health-check middleware dispatches CheckHealthAsync
        // reflectively, so the check's ctor gains no incoming edge — the ctor-assigned _configuration and the
        // private helper CheckHealthAsync calls CASCADE to false positives.
        var off = await FindingsIn(ns, WithAspNetCore(false));
        Assert.Contains(helper, off);
        Assert.Contains(ns + ".ConfigurationHealthCheck._configuration", off);

        // Plugin ON: the plugin roots CheckHealthAsync + instance ctors → the field and the private helper it
        // calls gain liveness → NOT reported…
        var on = await FindingsIn(ns, WithAspNetCore(true));
        Assert.DoesNotContain(helper, on);
        Assert.DoesNotContain(ns + ".ConfigurationHealthCheck._configuration", on);
        // …but the unrelated dead method STAYS FLAGGED (over-rooting guard).
        Assert.Contains(ns + ".ConfigurationHealthCheck.NeverProbed()", on);
    }

    // ── aspnetcore differential + over-rooting guard: authorization policy provider (H19) ────────────
    [Fact]
    public async Task AspNetCore_policy_provider_entry_and_cascade_kept_alive_H19()
    {
        const string ns = "CatH.AspNetPolicyProvider";
        const string helper = ns + ".HintAuthorizationPolicyProvider.LagEntraIdPolicy()";

        // Plugin OFF (also the DEFAULT — it is opt-in): the authorization middleware dispatches the
        // Get*PolicyAsync entry methods reflectively, so the provider's ctor gains no incoming edge — the
        // ctor-assigned _options and the private helper GetPolicyAsync calls CASCADE to false positives.
        var off = await FindingsIn(ns, WithAspNetCore(false));
        Assert.Contains(helper, off);
        Assert.Contains(ns + ".HintAuthorizationPolicyProvider._options", off);

        // Plugin ON: explicit fixture aliases preserve the policy-provider entry methods and instance ctors,
        // so the field and private helper gain liveness.
        var on = await FindingsIn(ns, WithAspNetCore(true));
        Assert.DoesNotContain(helper, on);
        Assert.DoesNotContain(ns + ".HintAuthorizationPolicyProvider._options", on);
        // …but the unrelated dead method STAYS FLAGGED (over-rooting guard).
        Assert.Contains(ns + ".HintAuthorizationPolicyProvider.NeverConsulted()", on);
    }

    [Fact]
    public async Task Resolved_framework_shapes_only_root_supported_conventions()
    {
        var config = new KnipConfig
        {
            Plugins = { ["blazorParameter"] = new PluginSettings { Enabled = true } },
        };

        var findings = await FindingsIn("CatH.QualifiedFrameworkShapes", config);

        Assert.Contains("CatH.QualifiedFrameworkShapes.FrameworkEndpoint.Routed()", findings);
        Assert.Contains("CatH.QualifiedFrameworkShapes.FrameworkEndpoint.RoutedCore()", findings);
        Assert.DoesNotContain("CatH.QualifiedFrameworkShapes.FrameworkComponent.Value", findings);
        Assert.DoesNotContain("CatH.QualifiedFrameworkShapes.FrameworkComponent.InitializeCore()", findings);
        Assert.Contains("CatH.QualifiedFrameworkShapes.FrameworkEndpoint.NeverCalled()", findings);
        Assert.Contains("CatH.QualifiedFrameworkShapes.FrameworkComponent.NeverRendered()", findings);
    }

    [Fact]
    public async Task Framework_controller_roots_actions_and_activation_without_blanket_members()
    {
        var findings = await FindingsIn("CatH.FrameworkControllerEntry", new KnipConfig());

        Assert.Equal(
            new HashSet<string>
            {
                "CatH.FrameworkControllerEntry.GhostController",
                "CatH.FrameworkControllerEntry.OrdersController.PublicHelper()",
                "CatH.FrameworkControllerEntry.OrdersController.ProtectedHelper()",
                "CatH.FrameworkControllerEntry.OrdersController.GenericHelper<T>()",
                "CatH.FrameworkControllerEntry.OrdersController.StaticHelper()",
                "CatH.FrameworkControllerEntry.PlainController.PublicHelper()",
                "CatH.FrameworkControllerEntry.PlainController.ProtectedHelper()",
                "CatH.FrameworkControllerEntry.AttributedEndpoint.PublicHelper()",
                "CatH.FrameworkControllerEntry.IgnoredController",
                "CatH.FrameworkControllerEntry.InternalController",
            },
            findings);
    }

    [Fact]
    public async Task Framework_component_roots_lifecycle_and_activation_without_blanket_members()
    {
        var findings = await FindingsIn("CatH.FrameworkComponentEntry", new KnipConfig());

        Assert.Equal(
            new HashSet<string>
            {
                "CatH.FrameworkComponentEntry.DashboardComponent.PublicHelper()",
                "CatH.FrameworkComponentEntry.DashboardComponent.ProtectedHelper()",
            },
            findings);
    }

    [Fact]
    public async Task Framework_hub_roots_hub_methods_and_activation_without_blanket_members()
    {
        var findings = await FindingsIn("CatH.FrameworkHubEntry", new KnipConfig());

        Assert.Equal(
            new HashSet<string>
            {
                "CatH.FrameworkHubEntry.ChatHub.ProtectedHelper()",
            },
            findings);
    }

    [Fact]
    public async Task Framework_page_model_roots_handlers_and_activation_without_blanket_members()
    {
        var findings = await FindingsIn("CatH.FrameworkPageModelEntry", new KnipConfig());

        Assert.Equal(
            new HashSet<string>
            {
                "CatH.FrameworkPageModelEntry.IndexModel.OnPostHelper()",
                "CatH.FrameworkPageModelEntry.IndexModel.PublicHelper()",
                "CatH.FrameworkPageModelEntry.IndexModel.Onboarding()",
                "CatH.FrameworkPageModelEntry.IndexModel.ProtectedHelper()",
            },
            findings);
    }

    [Fact]
    public async Task Framework_hosted_service_roots_lifecycle_and_activation_without_blanket_members()
    {
        var findings = await FindingsIn("CatH.FrameworkHostedServiceEntry", new KnipConfig());

        Assert.Equal(
            new HashSet<string>
            {
                "CatH.FrameworkHostedServiceEntry.Worker.PublicHelper()",
                "CatH.FrameworkHostedServiceEntry.Worker.ProtectedHelper()",
                "CatH.FrameworkHostedServiceEntry.DirectHostedService.PublicHelper()",
            },
            findings);
    }

    [Fact]
    public async Task Framework_plugins_ignore_unqualified_user_defined_shapes()
    {
        var config = new KnipConfig
        {
            Plugins =
            {
                ["blazorParameter"] = new PluginSettings { Enabled = true },
                ["serialization"] = new PluginSettings { Enabled = true },
            },
        };

        var findings = await FindingsIn("CatH.QualifiedCollisions", config);

        Assert.Contains("CatH.QualifiedCollisions.Handler.HandleCore()", findings);
        Assert.Contains("CatH.QualifiedCollisions.MappingProfile.ConfigureMap()", findings);
        Assert.Contains("CatH.QualifiedCollisions.Component.InitializeCore()", findings);
        Assert.Contains("CatH.QualifiedCollisions.Component.Value", findings);
        Assert.Contains("CatH.QualifiedCollisions.Dto.Value", findings);
        Assert.Contains("CatH.QualifiedCollisions.Middleware.Invoke()", findings);
    }

    // aspnetcore is ON by default (decided 2026-07-15): a default config keeps the middleware Invoke alive.
    [Fact]
    public async Task AspNetCore_is_on_by_default_without_matching_unqualified_stand_ins()
    {
        Assert.Contains("aspnetcore", PluginRegistry.DefaultEnabledIds);
        Assert.True(new KnipConfig().IsPluginEnabled(Descriptor("aspnetcore")),
            "aspnetcore must default ON");

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
    public void DefaultEnabledSet_is_reflection_scanningDi_aspnetcore()
    {
        // The shipped default-on set: reflection + scanningDi + aspnetcore (aspnetcore added by the
        // 2026-07-15 decision after dogfooding). blazorParameter/serialization stay opt-in. This pins
        // the seam's default so a regression (accidentally enabling/disabling a plugin) is caught.
        Assert.Equal(new[] { "reflection", "scanningDi", "aspnetcore" }, PluginRegistry.DefaultEnabledIds);

        var config = new KnipConfig();
        Assert.True(config.IsPluginEnabled(Descriptor("reflection")), "reflection must default ON");
        Assert.True(config.IsPluginEnabled(Descriptor("scanningDi")), "scanningDi must default ON");
        Assert.True(config.IsPluginEnabled(Descriptor("aspnetcore")), "aspnetcore must default ON");
        Assert.False(config.IsPluginEnabled(Descriptor("blazorParameter")), "blazorParameter opt-in");
        Assert.False(config.IsPluginEnabled(Descriptor("serialization")), "serialization opt-in");
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
        Assert.Empty(WithScanningDi(true).ValidatePlugins());
        Assert.Empty(WithBlazorParameter(true).ValidatePlugins());
        Assert.Empty(WithSerialization(true).ValidatePlugins());
        Assert.Empty(WithAspNetCore(true).ValidatePlugins());
    }

    private static PluginDescriptor Descriptor(string id) =>
        PluginRegistry.All.Single(d => d.Id == id);
}
