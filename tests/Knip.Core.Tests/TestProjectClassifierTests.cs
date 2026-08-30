using Knip.Core.Analysis;
using Knip.Core.Configuration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// K7 — test-project classification, one case per SIGNAL (first match wins). Signal 2 (referenced
/// test-framework assembly) cannot be exercised by the offline, zero-NuGet fixtures, so it is pinned
/// here with in-memory compilations: an assembly literally named "xunit.core" is synthesized and
/// referenced. Signals 1/3/default are also unit-pinned; the CatK fixture covers 1/3 end-to-end.
/// </summary>
public sealed class TestProjectClassifierTests
{
    private static Compilation EmptyCompilation(string assemblyName) =>
        CSharpCompilation.Create(assemblyName);

    /// <summary>A compilation that REFERENCES an assembly named <paramref name="referencedAssembly"/>.</summary>
    private static Compilation CompilationReferencing(string assemblyName, string referencedAssembly)
    {
        var referenced = CSharpCompilation.Create(
            referencedAssembly,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return CSharpCompilation.Create(assemblyName).AddReferences(referenced.ToMetadataReference());
    }

    [Fact] // Signal 1: explicit testProjects glob wins over EVERYTHING (even a production name).
    public void Explicit_testProjects_glob_is_the_highest_priority_signal()
    {
        var config = new KnipConfig { TestProjects = ["My.*.Verification"] };
        var result = TestProjectClassifier.Classify("My.App.Verification", EmptyCompilation("My.App.Verification"), config);

        Assert.Equal(ProjectKind.Test, result.Kind);
        Assert.Equal("testProjects:My.*.Verification", result.Signal);
    }

    [Theory] // Signal 2: a referenced test-framework assembly classifies as test (offline via synthesis).
    [InlineData("xunit.core")]
    [InlineData("nunit.framework")]
    [InlineData("Microsoft.VisualStudio.TestPlatform.TestFramework")]
    public void Referenced_test_framework_assembly_classifies_test(string framework)
    {
        // Project NAME is production-shaped ("Acme.App") so ONLY the assembly signal can classify it.
        var compilation = CompilationReferencing("Acme.App", framework);
        var result = TestProjectClassifier.Classify("Acme.App", compilation, new KnipConfig());

        Assert.Equal(ProjectKind.Test, result.Kind);
        Assert.Equal($"referencedAssembly:{framework}", result.Signal);
    }

    [Fact] // Signal 1 beats signal 2: config glob wins even when a test-framework assembly is referenced.
    public void Config_glob_beats_referenced_assembly()
    {
        var config = new KnipConfig { TestProjects = ["Acme.App"] };
        var compilation = CompilationReferencing("Acme.App", "xunit.core");
        var result = TestProjectClassifier.Classify("Acme.App", compilation, config);

        Assert.Equal("testProjects:Acme.App", result.Signal); // config, not referencedAssembly
    }

    [Theory] // Signal 3: name-glob fallback (no config, no test-framework assembly). Globs are tried in
             // order (*Tests, *.Test, *.Tests) — "*Tests" matches "…Tests" first (first match wins).
    [InlineData("Acme.App.Tests", "*Tests")]
    [InlineData("AcmeTests", "*Tests")]
    [InlineData("Acme.App.Test", "*.Test")]
    public void Name_glob_is_the_fallback_signal(string projectName, string expectedGlob)
    {
        var result = TestProjectClassifier.Classify(projectName, EmptyCompilation(projectName), new KnipConfig());

        Assert.Equal(ProjectKind.Test, result.Kind);
        Assert.Equal($"nameGlob:{expectedGlob}", result.Signal);
    }

    [Fact] // No signal matches -> production, signal "default".
    public void No_signal_classifies_production_default()
    {
        var result = TestProjectClassifier.Classify("Acme.App", EmptyCompilation("Acme.App"), new KnipConfig());

        Assert.Equal(ProjectKind.Production, result.Kind);
        Assert.Equal("default", result.Signal);
    }
}
