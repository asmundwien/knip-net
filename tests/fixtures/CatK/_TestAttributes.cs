using System;

namespace CatK;

// Local test-framework attributes so Category K needs ZERO NuGet: the reachability walker matches
// entry-point attributes by NAME (with/without the "Attribute" suffix), so a source-local FactAttribute
// named "Fact" is treated exactly like xunit's. Shared across the single-project K scenarios.
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class TheoryAttribute : Attribute { }
