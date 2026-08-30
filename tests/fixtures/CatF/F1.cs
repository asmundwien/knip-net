using System;

namespace CatF.F1;

// F1: local [Fact]/[Theory] stand-ins are explicit configured aliases. Rooting each method also roots its
// containing type without teaching the built-in defaults to trust every same-named attribute.
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
