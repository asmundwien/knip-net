using Knip.Core.Configuration;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Category F — entry points / roots. All rows are Contract: they must be GREEN. Each scenario asserts
/// the EXACT finding set for its namespace (what IS flagged and, by exclusion, what is rooted/alive).
/// Every ALIVE/rooted assertion ships with a DEAD SIBLING in the same fixture (a similar member that is
/// NOT an entry point and stays dead) or, for F8, RED-FLIP evidence (default-rooted -> flagged when the
/// config is emptied). Because different scenarios exercise different entry-point rules, each test
/// constructs its own <see cref="KnipConfig"/> and passes it to the runner.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class CatFTests
{
    private const string Category = "CatF";

    private static Task<IReadOnlySet<string>> FindingsIn(string ns, KnipConfig? config = null) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns, config ?? new KnipConfig(), includeSyntheticGlobalRoots: false);

    private static KnipConfig WithAttributeAliases(params string[] attributes) => new()
    {
        EntryPoints = new EntryPointConfig { Attributes = [.. attributes] },
    };

    [Fact] // F1: configured [Fact]/[Theory] aliases root methods and their containing type;
           // the non-test sibling remains flagged.
    public async Task F1_test_attribute_roots_method_and_type()
    {
        var findings = await FindingsIn("CatF.F1", WithAttributeAliases("Fact", "Theory"));
        Assert.Equal(
            new HashSet<string> { "CatF.F1.Tests.NotATest()" },
            findings);
    }

    [Fact] // F2: an explicitly configured *Controller pattern roots the type and public members;
           // private members and non-matching types stay dead.
    public async Task F2_configured_controller_name_pattern_roots_type_and_public_members()
    {
        var config = new KnipConfig
        {
            EntryPoints = new EntryPointConfig { NamePatterns = ["*Controller"] },
        };
        var findings = await FindingsIn("CatF.F2", config);
        Assert.Equal(
            new HashSet<string> { "CatF.F2.FooController.Helper()", "CatF.F2.FooService" },
            findings);
    }

    [Fact] // F3: subtype of a configured baseType roots type + public members; private member and a
           // non-subtype stay dead (dead-siblings).
    public async Task F3_configured_base_type_roots_subtype()
    {
        var config = new KnipConfig
        {
            EntryPoints = new EntryPointConfig { BaseTypes = ["CatF.F3.ControllerBase"] },
        };
        var findings = await FindingsIn("CatF.F3", config);
        Assert.Equal(
            new HashSet<string> { "CatF.F3.WidgetEndpoint.Internal()", "CatF.F3.WidgetHelper" },
            findings);
    }

    [Fact] // F4: implementer of a configured interface roots type + public members; private
           // (non-implementing) member and a non-implementer stay dead (dead-siblings).
    public async Task F4_configured_interface_roots_implementer()
    {
        var config = new KnipConfig
        {
            EntryPoints = new EntryPointConfig { ImplementedInterfaces = ["CatF.F4.IHostedService"] },
        };
        var findings = await FindingsIn("CatF.F4", config);
        // Worker (rooted by the configured interface) and Worker.Start() (public member of the entry type,
        // and an interface impl -> never reported) stay ALIVE. The interface's OWN method declaration
        // IHostedService.Start() is flagged: the engine roots the IMPLEMENTER's members, not the
        // interface's declared members, and nothing references the interface method itself. The interface
        // TYPE stays alive (Worker's implements-edge). Dead-siblings: Worker.Prepare() (private) + Bystander.
        Assert.Equal(
            new HashSet<string>
            {
                "CatF.F4.IHostedService.Start()",
                "CatF.F4.Worker.Prepare()",
                "CatF.F4.Bystander",
            },
            findings);
    }

    [Fact] // F5: attribute config matches WITH ("SuffixedAttribute") and WITHOUT ("Bare") the suffix;
           // both marked methods rooted, unmarked sibling flagged (dead-sibling).
    public async Task F5_attribute_matches_with_and_without_suffix()
    {
        var config = new KnipConfig
        {
            EntryPoints = new EntryPointConfig { Attributes = ["Bare", "SuffixedAttribute"] },
        };
        var findings = await FindingsIn("CatF.F5", config);
        Assert.Equal(
            new HashSet<string> { "CatF.F5.Endpoints.Unmarked()" },
            findings);
    }

    [Fact] // F6: top-level statements -> synthesized Main + Program host rooted (never flagged); an
           // unused CatF.F6 type is the dead-sibling proving the run analyzed the compilation.
    public async Task F6_top_level_statements_root_main_and_host()
    {
        // Scenario-scoped: the CatF.F6 orphan is flagged.
        var scoped = await FindingsIn("CatF.F6"); // default config
        Assert.Equal(
            new HashSet<string> { "CatF.F6.Orphan" },
            scoped);

        // Whole-set: the synthesized entry point stays an internal semantic root and is never reported.
        var all = await FixtureRunner.FindingSymbolsAsync(Category, new KnipConfig(), includeSyntheticGlobalRoots: false);
        Assert.DoesNotContain(all, s => s.Contains("<Main>$", StringComparison.Ordinal));

    }

    [Fact] // F7: a configured SymbolName ("CustomEntryPoint", non-default) roots the method + its type;
           // differently-named sibling flagged (dead-sibling).
    public async Task F7_configured_symbol_name_roots_member()
    {
        var config = new KnipConfig
        {
            EntryPoints = new EntryPointConfig { SymbolNames = ["CustomEntryPoint"] },
        };
        var findings = await FindingsIn("CatF.F7", config);
        Assert.Equal(
            new HashSet<string> { "CatF.F7.Startup.Other()" },
            findings);
    }

    [Fact] // F8: controller-like names are not framework roots without MVC evidence. The broad custom
           // name-pattern escape hatch still roots the type when explicitly configured.
    public async Task F8_controller_name_requires_framework_evidence_or_explicit_pattern()
    {
        var byDefault = await FindingsIn("CatF.F8");
        Assert.Equal(
            new HashSet<string> { "CatF.F8.EmptyProbeController" },
            byDefault);

        var explicitPattern = new KnipConfig
        {
            EntryPoints = new EntryPointConfig { NamePatterns = ["*Controller"] },
        };
        var configured = await FindingsIn("CatF.F8", explicitPattern);
        Assert.Empty(configured);
    }

    [Fact] // F9: an explicit local [TestInitialize] alias roots setup and its helper; an unattributed
           // sibling remains flagged, proving the attribute drives rooting.
    public async Task F9_mstest_testinitialize_roots_setup_and_helper()
    {
        var findings = await FindingsIn("CatF.F9", WithAttributeAliases("TestInitialize"));
        Assert.Equal(
            new HashSet<string> { "CatF.F9.LifecycleTests.UnattributedSetup()" },
            findings);
    }

    [Fact] // F10: explicit local MSTest lifecycle aliases root their hooks; an unattributed static sibling
           // remains flagged.
    public async Task F10_mstest_static_hooks_and_datatestmethod_use_explicit_aliases()
    {
        var findings = await FindingsIn(
            "CatF.F10",
            WithAttributeAliases("ClassInitialize", "AssemblyInitialize", "DataTestMethod"));
        Assert.Equal(
            new HashSet<string> { "CatF.F10.StaticHooks.UnattributedStaticSetup()" },
            findings);
    }

    [Fact] // F11: explicit local NUnit lifecycle aliases root their hooks; an unattributed sibling remains
           // flagged.
    public async Task F11_nunit_one_time_hooks_use_explicit_aliases()
    {
        var findings = await FindingsIn(
            "CatF.F11",
            WithAttributeAliases("OneTimeSetUp", "OneTimeTearDown"));
        Assert.Equal(
            new HashSet<string> { "CatF.F11.Fixture.NotAHook()" },
            findings);
    }

    [Fact] // F12 (FIX #4a): a [Fact] test class's instance ctor is rooted (the framework news the class
           // per test), so a field assigned only in the ctor and a helper called only from the ctor stay
           // ALIVE; a never-assigned/read field is the dead-sibling.
    public async Task F12_fact_class_ctor_keeps_ctor_only_field_and_helper_alive()
    {
        var findings = await FindingsIn("CatF.F12", WithAttributeAliases("Fact"));
        Assert.Equal(
            new HashSet<string> { "CatF.F12.SampleTests._neverUsed" },
            findings);
    }

    [Fact] // F13: an explicitly configured entry type is runtime-activated, so its ctor closure stays alive.
    public async Task F13_entry_type_ctor_keeps_ctor_only_field_and_helper_alive()
    {
        var config = new KnipConfig
        {
            EntryPoints = new EntryPointConfig { NamePatterns = ["*Controller"] },
        };
        var findings = await FindingsIn("CatF.F13", config);
        Assert.Equal(
            new HashSet<string> { "CatF.F13.WidgetController._unusedField" },
            findings);
    }

    [Fact] // F14: built-in host conventions are semantic, not global simple-name roots. The actual
           // top-level Main and Startup methods stay alive; same-name ordinary methods remain dead.
    public async Task F14_conventional_names_are_scoped_to_semantic_hosts()
    {
        var findings = await FindingsIn("CatF.F14");
        Assert.Equal(
            new HashSet<string>
            {
                "CatF.F14.Startup.Other()",
                "CatF.F14.Startup.Configure(string)",
                "CatF.F14.OrdinaryHost.Main()",
                "CatF.F14.OrdinaryHost.Main(string[])",
                "CatF.F14.OrdinaryHost.Configure()",
                "CatF.F14.OrdinaryHost.ConfigureServices()",
                "CatF.F14.OrdinaryHost.ConfigureContainer()",
            },
            findings);
    }

    [Fact] // F15: Roslyn's selected ordinary Main is the root. Other valid Main overloads on an alive
           // ordinary type remain reportable even when StartupObject resolves compiler ambiguity.
    public async Task F15_only_the_selected_compilation_entry_point_is_rooted()
    {
        var findings = await FindingsIn("CatF.F15");
        Assert.Equal(
            new HashSet<string>
            {
                "CatF.F15.OrdinaryHost.Main()",
                "CatF.F15.OrdinaryHost.Main(string[])",
            },
            findings);
    }

    [Fact]
    public async Task F16_user_defined_route_attribute_is_not_a_framework_entry_point()
    {
        var config = new KnipConfig { EntryPoints = new EntryPointConfig { SymbolNames = ["KeepAlive"] } };
        var findings = await FindingsIn("CatF.F16", config);

        var aliasConfig = WithAttributeAliases("CatF.F16.Route");
        aliasConfig.EntryPoints.SymbolNames.Add("KeepAlive");
        var aliased = await FindingsIn("CatF.F16", aliasConfig);
        Assert.Empty(aliased);

        Assert.Equal(
            new HashSet<string>
            {
                "CatF.F16.RouteAttribute",
                "CatF.F16.Endpoint.UserDefinedRoute()",
            },
            findings);
    }

    [Fact]
    public async Task F17_real_mstest_assembly_roots_test_methods()
    {
        var findings = await FindingsIn("CatF.F17");
        Assert.Equal(
            new HashSet<string> { "CatF.F17.MstestTests.DeadSibling()" },
            findings);
    }

}
