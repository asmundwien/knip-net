namespace CatD.D13;

using System;

[AttributeUsage(AttributeTargets.ReturnValue)]
public sealed class UsedReturnAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.ReturnValue)]
public sealed class UnusedReturnAttribute : Attribute
{
}

public sealed class Runner
{
    public void ConfigureServices()
    {
        _ = Produce();
    }

    [return: UsedReturn]
    private static int Produce() => 0;
}
