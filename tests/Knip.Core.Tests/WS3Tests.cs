using System.Diagnostics;
using Knip.Core.Model;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// WS3 — unused &lt;PackageReference&gt; detection. Ordinary packages are graded against their own compile
/// surface; only packages without one consult their dependency closure. The fixture covers:
/// <list type="bullet">
///   <item>an actually used ordinary package;</item>
///   <item>a used metapackage whose dependency provides the referenced assembly;</item>
///   <item>an unused ordinary package whose NETStandard dependency is used;</item>
///   <item>an unused ordinary package with no dependencies;</item>
///   <item>a build-only package and a source generator, both emitted at low confidence;</item>
///   <item>a used transitive-only package, which is never reported as a direct reference.</item>
/// </list>
/// The fixture is restored on demand because package attribution reads <c>project.assets.json</c>.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class WS3Tests
{
    private const string Category = "WS3";
    private static readonly SemaphoreSlim RestoreLock = new(1, 1);
    private static bool _restored;

    private static async Task<IReadOnlyList<Finding>> PackageReferenceFindingsAsync()
    {
        await EnsureRestored();
        var result = await FixtureRunner.RunAsync(Category);
        return result.Findings
            .Where(f => f.Kind == FindingKind.UnusedPackageReference)
            .ToList();
    }

    [Fact] // The declared-but-never-touched package IS reported, with the removePackageReference remediation.
    public async Task Unused_package_reference_is_reported()
    {
        var findings = await PackageReferenceFindingsAsync();

        var unused = Assert.Single(findings, f => f.Symbol == "Humanizer.Core");
        Assert.Equal(FindingKind.UnusedPackageReference, unused.Kind);
        Assert.Equal("WS3.App", unused.Project);
        Assert.Equal("package reference", unused.SymbolKind);
        Assert.Equal(Remediation.RemovePackageReference, unused.Remediation);
        Assert.EndsWith("WS3.App.csproj", unused.FilePath);
        // A normal package-ref (delivers a compile assembly) is conservative-by-construction: C3 → medium.
        Assert.Equal(Confidence.Medium, unused.Confidence);
        Assert.DoesNotContain(Hazard.BuildOnlyPackage, unused.Hazards);
        // The deletion unit points at the <PackageReference/> element in the .csproj.
        Assert.NotNull(unused.Span);
        Assert.EndsWith("WS3.App.csproj", unused.Span!.File);
    }

    [Fact]
    public async Task Unused_ordinary_package_is_reported_when_only_its_netstandard_dependency_is_used()
    {
        var findings = await PackageReferenceFindingsAsync();

        var unused = Assert.Single(
            findings,
            f => f.Symbol == "Swashbuckle.AspNetCore.Swagger");
        Assert.Equal(Confidence.Medium, unused.Confidence);
        Assert.DoesNotContain(Hazard.BuildOnlyPackage, unused.Hazards);
    }

    [Fact] // The package whose assembly Program.Main actually touches is NOT reported (false-positive guard).
    public async Task Used_package_reference_is_not_reported()
    {
        var findings = await PackageReferenceFindingsAsync();

        Assert.DoesNotContain(findings, f => f.Symbol == "Newtonsoft.Json");
    }

    [Fact]
    public async Task Transitive_only_package_is_not_reported_as_a_direct_reference()
    {
        var findings = await PackageReferenceFindingsAsync();

        Assert.DoesNotContain(findings, f => f.Symbol == "Microsoft.OpenApi");
    }

    [Fact] // METAPACKAGE regression guard: a package with an EMPTY own compile set whose DEPENDENCY delivers
    // the used assembly (Swashbuckle.AspNetCore -> ...SwaggerGen) is NOT flagged unused and NOT mis-tagged
    // build-only. Graded against the dependency closure (assets `dependencies` graph).
    public async Task Used_metapackage_is_not_reported()
    {
        var findings = await PackageReferenceFindingsAsync();

        Assert.DoesNotContain(findings, f => f.Symbol == "Swashbuckle.AspNetCore");
    }

    [Fact]
    public async Task Build_only_package_is_emitted_with_hazard_and_low_confidence()
    {
        var findings = await PackageReferenceFindingsAsync();

        var buildOnly = Assert.Single(
            findings,
            f => f.Symbol == "Microsoft.Extensions.ApiDescription.Server");
        Assert.Equal(Remediation.RemovePackageReference, buildOnly.Remediation);
        Assert.Contains(Hazard.BuildOnlyPackage, buildOnly.Hazards);
        Assert.Equal(Confidence.Low, buildOnly.Confidence);
    }

    [Fact] // HAZARD: source-generator / PrivateAssets package is EMITTED with hazard + LOW confidence
    // (assert the tier, not absence — REVISED §3.8: never silently dropped).
    public async Task Source_generator_is_emitted_with_hazard_and_low_confidence()
    {
        var findings = await PackageReferenceFindingsAsync();

        var buildOnly = Assert.Single(findings, f => f.Symbol == "PolySharp");
        Assert.Equal(Remediation.RemovePackageReference, buildOnly.Remediation);
        Assert.Contains(Hazard.BuildOnlyPackage, buildOnly.Hazards);
        Assert.Equal(Confidence.Low, buildOnly.Confidence);
    }

    [Fact]
    public async Task Exactly_the_unused_and_build_only_packages_are_flagged()
    {
        var findings = await PackageReferenceFindingsAsync();

        Assert.Equal(
            new[]
            {
                "Humanizer.Core",
                "Microsoft.Extensions.ApiDescription.Server",
                "PolySharp",
                "Swashbuckle.AspNetCore.Swagger",
            },
            findings.Select(f => f.Symbol).OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Restore the fixture once per test process so obj/project.assets.json exists (offline caveat).</summary>
    private static async Task EnsureRestored()
    {
        if (_restored) return;
        await RestoreLock.WaitAsync();
        try
        {
            if (_restored) return;
            var csproj = Path.Combine(
                Path.GetDirectoryName(FixtureRunner.ResolveFixtureSolution(Category))!, "App", "WS3.App.csproj");

            var psi = new ProcessStartInfo("dotnet", $"restore \"{csproj}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"WS3 fixture restore failed (exit {process.ExitCode}). This test needs nuget.org-cached " +
                    $"packages.\nstdout:\n{stdout}\nstderr:\n{stderr}");

            _restored = true;
        }
        finally
        {
            RestoreLock.Release();
        }
    }
}
