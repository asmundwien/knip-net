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
        FixtureRunner.FindingSymbolsInAsync(Category, ns, config);

    [Fact] // F1: [Fact]/[Theory] method + containing type ALIVE (default Attributes include Fact/Theory);
           // non-test sibling flagged (dead-sibling).
    public async Task F1_test_attribute_roots_method_and_type()
    {
        var findings = await FindingsIn("CatF.F1"); // default config
        Assert.Equal(
            new HashSet<string> { "CatF.F1.Tests.NotATest()" },
            findings);
    }

    [Fact] // F2: *Controller name pattern (default) roots type + public members; private member and a
           // non-controller type stay dead (dead-siblings).
    public async Task F2_controller_name_pattern_roots_type_and_public_members()
    {
        var findings = await FindingsIn("CatF.F2"); // default config
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

        // Whole-set: the synthesized entry point / host type is rooted, so nothing named Program or the
        // synthesized <Main>$ (also filtered as compiler-generated) may appear anywhere in the findings.
        var all = await FixtureRunner.FindingSymbolsAsync(Category);
        Assert.DoesNotContain(all, s => s.Contains("Program") || s.Contains("Main"));
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

    [Fact] // F8: RED-FLIP. Rooted by DEFAULT config (*Controller), then flagged once EntryPoints is
           // emptied -> proves the config actually drives rooting.
    public async Task F8_emptied_entry_points_flip_previously_rooted_to_dead()
    {
        // Baseline: default config roots the *Controller type, so only the private-less public member
        // is alive and the scenario is clean (nothing flagged in CatF.F8).
        var rootedByDefault = await FindingsIn("CatF.F8"); // default config
        Assert.Empty(rootedByDefault);

        // Flip: all entry-point lists empty -> no default rooting -> the type is entirely dead and is
        // reported (outermost).
        var empty = new KnipConfig
        {
            EntryPoints = new EntryPointConfig
            {
                SymbolNames = [],
                Attributes = [],
                BaseTypes = [],
                ImplementedInterfaces = [],
                NamePatterns = [],
            },
        };
        var flipped = await FindingsIn("CatF.F8", empty);
        Assert.Equal(
            new HashSet<string> { "CatF.F8.EmptyProbeController" },
            flipped);
    }

    [Fact] // F9: MSTest [TestInitialize] setup method rooted by DEFAULT config -> keeps the helper it
           // calls and the containing type alive; an unattributed sibling is flagged (dead-sibling),
           // proving the attribute (not mere presence in a test class) is what roots it.
    public async Task F9_mstest_testinitialize_roots_setup_and_helper()
    {
        var findings = await FindingsIn("CatF.F9"); // default config
        Assert.Equal(
            new HashSet<string> { "CatF.F9.LifecycleTests.UnattributedSetup()" },
            findings);
    }

    [Fact] // F10: MSTest static [ClassInitialize]/[AssemblyInitialize] hooks and [DataTestMethod] are all
           // rooted by DEFAULT config; an unattributed static sibling is flagged (dead-sibling).
    public async Task F10_mstest_static_hooks_and_datatestmethod_rooted_by_default()
    {
        var findings = await FindingsIn("CatF.F10"); // default config
        Assert.Equal(
            new HashSet<string> { "CatF.F10.StaticHooks.UnattributedStaticSetup()" },
            findings);
    }

    [Fact] // F11: NUnit [OneTimeSetUp]/[OneTimeTearDown] hooks rooted by DEFAULT config; a same-shaped
           // unattributed sibling is flagged (dead-sibling).
    public async Task F11_nunit_one_time_hooks_rooted_by_default()
    {
        var findings = await FindingsIn("CatF.F11"); // default config
        Assert.Equal(
            new HashSet<string> { "CatF.F11.Fixture.NotAHook()" },
            findings);
    }
}
