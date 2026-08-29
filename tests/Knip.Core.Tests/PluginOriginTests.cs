using Knip.Core.Analysis;
using Microsoft.CodeAnalysis.CSharp;
using Knip.Core.Configuration;
using Knip.Core.Model;
using Xunit;

namespace Knip.Core.Tests;

[Collection(MsBuildCollection.Name)]
public sealed class PluginOriginTests
{
    [Fact]
    public async Task Production_mode_preserves_test_and_production_plugin_origins()
    {
        var result = await FixtureRunner.RunAsync("PluginOrigin", EnabledPlugins(production: true));

        AssertTestOnly(result, "PluginOrigin.Lib.Targets.TestReflection()");
        AssertTestOnly(result, "PluginOrigin.Lib.TestSerializationDto.Value");
        AssertTestOnly(result, "PluginOrigin.Lib.Targets.TestScanning()");
        AssertTestOnly(result, "PluginOrigin.Lib.Targets.TestBlazor()");
        AssertTestOnly(result, "PluginOrigin.Lib.TestMiddleware.Invoke()");

        Assert.DoesNotContain(result.Findings, finding => finding.Symbol == "PluginOrigin.Lib.Targets.ProductionReflection()");
        Assert.DoesNotContain(result.Findings, finding => finding.Symbol == "PluginOrigin.Lib.ProductionSerializationDto.Value");
        Assert.DoesNotContain(result.Findings, finding => finding.Symbol == "PluginOrigin.Lib.Targets.ProductionScanning()");
        Assert.DoesNotContain(result.Findings, finding => finding.Symbol == "PluginOrigin.Lib.Targets.ProductionBlazor()");
        Assert.DoesNotContain(result.Findings, finding => finding.Symbol == "PluginOrigin.Lib.ProductionMiddleware.Invoke()");
        Assert.Contains(result.Findings, finding =>
            finding.Symbol == "PluginOrigin.Lib.Targets.NeverUsed()"
            && finding.Kind == FindingKind.UnusedMethod);
    }
    [Fact]
    public async Task Default_mode_keeps_test_only_plugin_contributions_alive()
    {
        var result = await FixtureRunner.RunAsync("PluginOrigin", EnabledPlugins(production: false));

        AssertAlive(result, "PluginOrigin.Lib.Targets.TestReflection()");
        AssertAlive(result, "PluginOrigin.Lib.TestSerializationDto.Value");
        AssertAlive(result, "PluginOrigin.Lib.Targets.TestScanning()");
        AssertAlive(result, "PluginOrigin.Lib.Targets.TestBlazor()");
        AssertAlive(result, "PluginOrigin.Lib.TestMiddleware.Invoke()");
        Assert.Contains(result.Findings, finding =>
            finding.Symbol == "PluginOrigin.Lib.Targets.NeverUsed()"
            && finding.Kind == FindingKind.UnusedMethod);
    }

    [Fact]
    public void Plugin_edge_becomes_production_when_discovered_from_both_origins()
    {
        var compilation = CSharpCompilation.Create(
            "PluginEdgeFixture",
            [CSharpSyntaxTree.ParseText("public sealed class C { public void From() { } public void To() { } }")]);
        var type = compilation.GetTypeByMetadataName("C")!;
        var from = type.GetMembers("From").Single();
        var to = type.GetMembers("To").Single();
        var fromId = SymbolId.For(from)!;
        var toId = SymbolId.For(to)!;
        var state = new GraphState();
        var solutionAssemblies = new HashSet<string>(StringComparer.Ordinal) { compilation.AssemblyName! };

        new ContributionSink(state, solutionAssemblies, testProject: true).AddEdge(from, to);
        Assert.True(state.IsTestOnlyPluginEdge(fromId, toId));

        new ContributionSink(state, solutionAssemblies, testProject: false).AddEdge(from, to);
        Assert.False(state.IsTestOnlyPluginEdge(fromId, toId));
    }



    private static void AssertAlive(AnalysisResult result, string symbol) =>
        Assert.DoesNotContain(result.Findings, finding => finding.Symbol == symbol);
    private static void AssertTestOnly(AnalysisResult result, string symbol)
    {
        var finding = Assert.Single(result.Findings, finding => finding.Symbol == symbol);
        Assert.Equal(FindingKind.OnlyUsedByTests, finding.Kind);
    }

    private static KnipConfig EnabledPlugins(bool production)
    {
        var config = new KnipConfig { Production = production };
        foreach (var id in new[] { "reflection", "serialization", "scanningDi", "blazorParameter", "aspnetcore" })
            config.Plugins[id] = new PluginSettings { Enabled = true };
        return config;
    }
}
