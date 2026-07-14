using System;

namespace CatF.F5;

// F5: attribute config matches WITH and WITHOUT the "Attribute" suffix (MatchesAttribute trims the
// class-name suffix, then compares against the configured names either way).
//   - Config entry "Bare" (no suffix) matches the [Bare] attribute (class BareAttribute, trimmed "Bare").
//   - Config entry "SuffixedAttribute" (with suffix) matches the [Suffixed] attribute (class name equals).
// The test passes EntryPoints.Attributes = ["Bare", "SuffixedAttribute"].
[AttributeUsage(AttributeTargets.Method)]
public sealed class BareAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class SuffixedAttribute : Attribute { }

public sealed class Endpoints
{
    // ALIVE (root): matched by the bare config name "Bare".
    [Bare]
    public void RootedByBareName() { }

    // ALIVE (root): matched by the suffixed config name "SuffixedAttribute".
    [Suffixed]
    public void RootedBySuffixedName() { }

    // DEAD SIBLING: no entry-point attribute, uncalled -> flagged.
    public void Unmarked() { }
}
