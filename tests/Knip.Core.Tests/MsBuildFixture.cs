using Microsoft.Build.Locator;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// Registers an MSBuild instance exactly once per test process. MSBuildLocator throws if you
/// register twice, so every test that opens a fixture solution must share this one registration
/// via <see cref="MsBuildCollection"/>. Mirrors Knip.Cli/Program.cs — Knip.Core itself stays
/// locator-free (invariant #9), so the TEST assembly does the registration.
/// </summary>
public sealed class MsBuildFixture
{
    public MsBuildFixture()
    {
        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();
    }
}

[CollectionDefinition(Name)]
public sealed class MsBuildCollection : ICollectionFixture<MsBuildFixture>
{
    public const string Name = "msbuild";
}
