namespace CatD.D15;

using System;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UsedAccessorAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class UnusedAccessorAttribute : Attribute
{
}

public sealed class Runner
{
    public void ConfigureServices()
    {
        _ = Value;
    }

    private int Value
    {
        [UsedAccessor]
        get => 0;
    }
}
