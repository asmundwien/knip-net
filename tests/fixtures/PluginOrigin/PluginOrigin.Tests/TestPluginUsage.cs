using System;
using PluginOrigin.Lib;

namespace PluginOrigin.Tests;

internal static class TestPluginUsage
{
    public static void ConfigurePlugins()
    {
        _ = typeof(Targets).GetMethod(nameof(Targets.TestReflection));
        Serializer.Serialize<TestSerializationDto>(null!);
        new ServiceCollection().AddTransient<TestRegisteredService>();
        new MiddlewareBuilder().UseMiddleware<TestMiddleware>();
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

internal sealed class TestComponent
{
    [Parameter]
    public int Value => Targets.TestBlazor();
}
