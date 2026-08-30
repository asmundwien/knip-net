namespace CatK.K9;

// Shared-framework APIs keep runtime-hazard matching assembly- and namespace-aware without NuGet restore.

internal sealed class SerializedData
{
    internal string RuntimeValue { get; set; } = "";
}

internal sealed class PlainData
{
    internal string PlainValue { get; set; } = "";
}

internal sealed class ConfigBoundData
{
    public string RuntimeValue { get; set; } = "";
}

internal sealed class PlainConfigData
{
    public string PlainValue { get; set; } = "";
}

internal sealed class FactoryRegisteredService
{
    internal FactoryRegisteredService() => RuntimeDependency();

    internal static string RuntimeDependency() => "runtime";
}

internal sealed class PlainService
{
    internal static string PlainDependency() => "plain";
}

internal static class TestOnlyBoundary
{
    // Direct test boundary. Everything called here is transitive test-only production code.
    internal static string Run() =>
        new SerializedData().RuntimeValue
        + new PlainData().PlainValue
        + new ConfigBoundData().RuntimeValue
        + new PlainConfigData().PlainValue
        + FactoryRegisteredService.RuntimeDependency()
        + PlainService.PlainDependency();

    internal static void KeepAlive() { }
}

internal static class Entry
{
    internal static void Main()
    {
        // Keep all containing types production-reachable without reading their test-only members.
        System.Console.WriteLine(System.Text.Json.JsonSerializer.Deserialize<SerializedData>("{}"));
        Microsoft.Extensions.Configuration.IConfiguration configuration = null!;
        System.Console.WriteLine(
            Microsoft.Extensions.Configuration.ConfigurationBinder.Get<ConfigBoundData>(configuration));
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
            .AddScoped<FactoryRegisteredService>(services, _ => default!);
        System.Console.WriteLine(new PlainData());
        System.Console.WriteLine(new PlainConfigData());
        System.Console.WriteLine(typeof(PlainService));
        TestOnlyBoundary.KeepAlive();
    }
}

internal sealed class RuntimeHazardTests
{
    [CatK.Fact]
    internal void Exercises_boundary() => System.Console.WriteLine(TestOnlyBoundary.Run());
}
