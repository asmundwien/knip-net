using CatH.DiConstructorActivation.Metadata;

namespace CatH.DiConstructorActivation;

public interface IRegisteredService { }

public sealed class RegisteredService : IRegisteredService
{
    private readonly string _state;
    private readonly string _initializedState = BuildInitializedState();

    public RegisteredService()
    {
        _state = BuildState();
    }

    private static string BuildState() => "ready";

    private static string BuildInitializedState() => "initialized";

    public void NeverCalled() => System.Console.WriteLine(_state);
}

public interface ITypeRegisteredService { }

public sealed class TypeRegisteredService : ITypeRegisteredService
{
    private readonly string _state;

    public TypeRegisteredService()
    {
        _state = BuildState();
    }

    private static string BuildState() => "ready";

    public void NeverCalled() => System.Console.WriteLine(_state);
}

public sealed class SingleTypeRegisteredService
{
    private readonly string _state = BuildState();

    private static string BuildState() => "ready";

    public void NeverCalled() => System.Console.WriteLine(_state);
}

public interface IStaticRegisteredService { }

public sealed class StaticRegisteredService : IStaticRegisteredService
{
    private readonly string _state;

    public StaticRegisteredService()
    {
        _state = BuildState();
    }

    private static string BuildState() => "ready";

    public void NeverCalled() => System.Console.WriteLine(_state);
}

public abstract class RegisteredServiceBase
{
    private readonly string _constructedState;
    private readonly string _initializedState = BuildInitializedState();

    protected RegisteredServiceBase()
    {
        _constructedState = BuildConstructedState();
    }

    private static string BuildConstructedState() => "constructed";

    private static string BuildInitializedState() => "initialized";

    internal void NeverCalled() => System.Console.WriteLine(_constructedState + _initializedState);
}

public sealed class DerivedRegisteredService : RegisteredServiceBase { }

public sealed class FactoryRegisteredService
{
    private readonly string _state = BuildState();

    private static string BuildState() => "ready";

    internal void NeverCalled() => System.Console.WriteLine(_state);
}

public sealed class InstanceRegisteredService
{
    private readonly string _state;

    public InstanceRegisteredService()
    {
        _state = BuildState();
    }

    private static string BuildState() => "ready";

    internal void NeverCalled() => System.Console.WriteLine(_state);
}

public sealed class TryAddRegisteredService
{
    private readonly string _state = BuildState();

    private static string BuildState() => "ready";

    internal void NeverCalled() => System.Console.WriteLine(_state);
}

public sealed class TryAddFactoryRegisteredService
{
    private readonly string _state = BuildState();

    private static string BuildState() => "ready";

    internal void NeverCalled() => System.Console.WriteLine(_state);
}

public sealed class ServiceProvider { }

public sealed class ServiceCollection { }

public static class ServiceCollectionServiceExtensions
{
    public static void AddScoped<TService, TImplementation>(this ServiceCollection services)
        where TImplementation : TService
    {
    }

    public static void AddScoped<TService>(
        this ServiceCollection services, System.Func<ServiceProvider, TService> factory)
    {
    }

    public static void AddScoped(this ServiceCollection services, System.Type serviceType)
    {
    }

    public static void AddSingleton<TService>(this ServiceCollection services, TService instance)
    {
    }

    public static void AddTransient(
        this ServiceCollection services, System.Type serviceType, System.Type implementationType)
    {
    }
}

public static class ServiceCollectionDescriptorExtensions
{
    public static void TryAddScoped(this ServiceCollection services, System.Type service)
    {
    }

    public static void TryAddTransient(
        this ServiceCollection services,
        System.Type service,
        System.Func<ServiceProvider, object> implementationFactory)
    {
    }
}

public sealed class Startup
{
    public void ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRegisteredService, RegisteredService>();
        services.AddTransient(typeof(ITypeRegisteredService), typeof(TypeRegisteredService));
        services.AddScoped(typeof(SingleTypeRegisteredService));
        ServiceCollectionServiceExtensions.AddScoped<IStaticRegisteredService, StaticRegisteredService>(services);
        services.AddScoped<DerivedRegisteredService, DerivedRegisteredService>();
        services.AddScoped<FactoryRegisteredService>(_ => default!);
        services.AddSingleton<InstanceRegisteredService>(default!);
        services.TryAddScoped(typeof(TryAddRegisteredService));
        services.TryAddTransient(typeof(TryAddFactoryRegisteredService), _ => default!);
        services.AddScoped(typeof(MetadataRegisteredService));
        services.TryAddTransient(typeof(MetadataFactoryRegisteredService), _ => default!);
    }
}
