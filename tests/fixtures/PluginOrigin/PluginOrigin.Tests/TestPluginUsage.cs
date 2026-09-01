using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PluginOrigin.Lib;

namespace PluginOrigin.Tests;

internal static class TestPluginUsage
{
    public static void ConfigurePlugins(IApplicationBuilder app)
    {
        _ = typeof(Targets).GetMethod(nameof(Targets.TestReflection));
        JsonSerializer.Serialize<TestSerializationDto>(null!);
        new ServiceCollection().AddTransient<TestRegisteredService>();
        app.UseMiddleware<TestMiddleware>();
    }
}

internal sealed class TestComponent
{
    [Parameter]
    public int Value => Targets.TestBlazor();
}
