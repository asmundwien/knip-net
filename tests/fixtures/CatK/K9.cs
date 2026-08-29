namespace CatK.K9;

// Local stand-ins keep the fixture offline while matching the runtime-hazard detector's API shapes.
internal static class JsonConvert
{
    internal static T DeserializeObject<T>(string json) => default!;
}

internal interface IConfiguration { }

internal static class ConfigurationBinder
{
    internal static T Get<T>(this IConfiguration configuration) => default!;
}

internal sealed class ServiceProvider { }
internal sealed class ServiceCollection { }

internal static class ServiceCollectionServiceExtensions
{
    internal static void AddScoped<TService>(
        this ServiceCollection services,
        System.Func<ServiceProvider, TService> factory)
    {
    }
}

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
        System.Console.WriteLine(JsonConvert.DeserializeObject<SerializedData>("{}"));
        IConfiguration configuration = null!;
        System.Console.WriteLine(configuration.Get<ConfigBoundData>());
        var services = new ServiceCollection();
        services.AddScoped<FactoryRegisteredService>(_ => default!);
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
