using System;

namespace CatF.F1;

// F1: a [Fact]/[Theory] method is a root (default config Attributes include "Fact"/"Theory").
// Rooting the method also roots its containing type (EvaluateRoots walks ContainingType chain).
// Attributes match by NAME with/without the "Attribute" suffix, so a LOCAL FactAttribute suffices —
// no xunit package needed.
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class TheoryAttribute : Attribute { }

public sealed class Tests
{
    // ALIVE (root): [Fact] method -> keeps Tests type alive too.
    [Fact]
    public void FactTest() { }

    // ALIVE (root): [Theory] method.
    [Theory]
    public void TheoryTest() { }

    // DEAD SIBLING: identical shape, no test attribute, no caller -> flagged.
    public void NotATest() { }
}
