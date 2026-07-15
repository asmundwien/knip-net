using System.Diagnostics;
using Knip.Core.Model;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// WS3 — unused &lt;PackageReference&gt; detection. A package is UNUSED when none of the assemblies it
/// delivers is touched by any symbol in the referencing project. Per REVISED §3.8 (recall over silence),
/// build-only / analyzer / source-generator / PrivateAssets packages are EMITTED with a hazard + low
/// confidence, never dropped — so this fixture asserts the TIER, not absence.
///
/// Fixture (tests/fixtures/WS3, one project, never in Knip.slnx):
///   Newtonsoft.Json       : Program.Main serializes via JsonConvert (touches the assembly)    -> NOT flagged
///   Swashbuckle.AspNetCore: METAPACKAGE — empty own compile; its SwaggerGen DEPENDENCY delivers
///                           the SwaggerGenOptions assembly Program.Main touches (closure used)  -> NOT flagged
///   Humanizer.Core        : declared, no symbol ever touches the Humanizer assembly            -> FLAGGED (medium)
///   PolySharp             : analyzer / source-gen, PrivateAssets="all", no compile assembly    -> FLAGGED (low, hazard)
///
/// The Swashbuckle case is the metapackage regression guard: WS3 grades a declared package against its
/// DEPENDENCY CLOSURE (itself + transitive deps), so a metapackage whose own compile set is empty but
/// whose dependency packages deliver a used assembly is NOT reported unused nor mis-tagged build-only.
///
/// OFFLINE: WS3 reads obj/project.assets.json for the assembly→package map, so the fixture must be
/// restored. <see cref="EnsureRestored"/> runs `dotnet restore` once (nuget.org-cached packages only —
/// Newtonsoft.Json 13.0.3, Humanizer.Core 2.14.1, PolySharp 1.14.1, Swashbuckle.AspNetCore 7.2.0 + its
/// SwaggerGen/Swagger/SwaggerUI deps, all offline-friendly).
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

    [Fact] // The package whose assembly Program.Main actually touches is NOT reported (false-positive guard).
    public async Task Used_package_reference_is_not_reported()
    {
        var findings = await PackageReferenceFindingsAsync();

        Assert.DoesNotContain(findings, f => f.Symbol == "Newtonsoft.Json");
    }

    [Fact] // METAPACKAGE regression guard: a package with an EMPTY own compile set whose DEPENDENCY delivers
    // the used assembly (Swashbuckle.AspNetCore -> ...SwaggerGen) is NOT flagged unused and NOT mis-tagged
    // build-only. Graded against the dependency closure (assets `dependencies` graph).
    public async Task Used_metapackage_is_not_reported()
    {
        var findings = await PackageReferenceFindingsAsync();

        Assert.DoesNotContain(findings, f => f.Symbol == "Swashbuckle.AspNetCore");
    }

    [Fact] // HAZARD: analyzer / source-gen / PrivateAssets package is EMITTED with hazard + LOW confidence
    // (assert the tier, not absence — REVISED §3.8: never silently dropped).
    public async Task Build_only_package_is_emitted_with_hazard_and_low_confidence()
    {
        var findings = await PackageReferenceFindingsAsync();

        var buildOnly = Assert.Single(findings, f => f.Symbol == "PolySharp");
        Assert.Equal(Remediation.RemovePackageReference, buildOnly.Remediation);
        Assert.Contains(Hazard.BuildOnlyPackage, buildOnly.Hazards);
        Assert.Equal(Confidence.Low, buildOnly.Confidence);
    }

    [Fact] // The complete package-reference finding set for the fixture is exactly {Humanizer.Core, PolySharp}.
    public async Task Exactly_the_unused_and_build_only_packages_are_flagged()
    {
        var findings = await PackageReferenceFindingsAsync();

        Assert.Equal(
            new[] { "Humanizer.Core", "PolySharp" },
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
