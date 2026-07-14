using System;

namespace CatK.K4.Tests;

// Local FactAttribute (zero NuGet); matched by name by the reachability walker.
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute { }

// The ONLY caller of CatK.K4.Widget.TestOnly lives here, inside a [Fact] root. Ignoring this project
// (config.Ignore.Projects = ["*Tests*"]) removes the caller and flips TestOnly to a finding.
public sealed class WidgetTests
{
    [Fact]
    public void Exercises_TestOnly()
    {
        new global::CatK.K4.Widget().TestOnly();
    }
}
