using System;
using PluginOrigin.Lib;

namespace PluginOrigin.App;

internal static class Program
{
    public static void Main()
    {
        KeepType<Targets>();
        KeepType<TestSerializationDto>();
        KeepType<ProductionSerializationDto>();
        KeepType<TestRegisteredService>();
        KeepType<ProductionRegisteredService>();
        KeepType<TestMiddleware>();
        KeepType<ProductionMiddleware>();
    }

    private static void KeepType<T>() { }

    private static void ConfigurePlugins()
    {
        _ = typeof(Targets).GetMethod(nameof(Targets.ProductionReflection));
        Serializer.Serialize<ProductionSerializationDto>(null!);
        new ServiceCollection().AddTransient<ProductionRegisteredService>();
        new MiddlewareBuilder().UseMiddleware<ProductionMiddleware>();
    }
}

internal static class Serializer
{
    public static void Serialize<T>(T value) { }
}

internal sealed class ServiceCollection
{
    public void AddTransient<T>() where T : class { }
}

internal sealed class MiddlewareBuilder
{
    public void UseMiddleware<T>() where T : class { }
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class ParameterAttribute : Attribute { }

internal sealed class ProductionComponent
{
    [Parameter]
    public int Value => Targets.ProductionBlazor();
}
