using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
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

    private static void ConfigurePlugins(IApplicationBuilder app)
    {
        _ = typeof(Targets).GetMethod(nameof(Targets.ProductionReflection));
        JsonSerializer.Serialize<ProductionSerializationDto>(null!);
        new ServiceCollection().AddTransient<ProductionRegisteredService>();
        app.UseMiddleware<ProductionMiddleware>();
    }
}

internal sealed class ProductionComponent
{
    [Parameter]
    public int Value => Targets.ProductionBlazor();
}
