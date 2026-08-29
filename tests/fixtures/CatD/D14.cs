namespace CatD.D14;

using System;

[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class UsedTypeParameterAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class UnusedTypeParameterAttribute : Attribute
{
}

public sealed class Runner
{
    public void ConfigureServices()
    {
        Consume<int>();
    }

    private static void Consume<[UsedTypeParameter] T>()
    {
    }
}
