namespace CatD.D12;

using System;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UsedParameterAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UnusedParameterAttribute : Attribute
{
}

public sealed class Runner
{
    public void ConfigureServices()
    {
        Consume(0);
    }

    private static void Consume([UsedParameter] int value)
    {
    }
}
