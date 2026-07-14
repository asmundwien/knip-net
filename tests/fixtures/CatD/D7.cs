namespace CatD.D7;

using System;

// D7: an attribute class that is APPLIED somewhere is alive (edge from the decorated, live symbol to
// the attribute class via AddSignatureReferences' GetAttributes handling); an attribute class that is
// NEVER applied and never otherwise referenced is unreachable and flagged.
//
// Mechanism: MUTATION CHECK — this scenario's own used/unused pair is the check. UsedAttr is applied
// to the rooted Runner type (alive); UnusedAttr is applied nowhere (flagged). The pair is identical
// apart from the application site.
[AttributeUsage(AttributeTargets.Class)]
public sealed class UsedAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class UnusedAttribute : Attribute
{
}

// Runner is rooted via ConfigureServices; applying [Used] here keeps UsedAttribute alive.
[Used]
public sealed class Runner
{
    public void ConfigureServices()
    {
    }
}
