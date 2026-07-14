namespace CatC.C3;

// C3: DI-style registration AddScoped<IFoo, Foo>() keeps Foo alive via a generic-argument edge
// (GenericName at the use site records an edge to the constructed method, whose type arguments
// Foo/IFoo become type-reference edges). AddScoped is mimicked locally to stay offline.
public interface IFoo { }

// ALIVE: appears as the TImpl generic argument of AddScoped<IFoo, Foo>() below.
public sealed class Foo : IFoo { }

// DEAD SIBLING: same shape as Foo, implements IFoo, but never registered/referenced -> flagged.
public sealed class Bar : IFoo { }

public sealed class Sample
{
    // Local mimic of services.AddScoped<TI, TImpl>(); empty body, no NuGet.
    private static void AddScoped<TI, TImpl>() where TImpl : TI { }

    public void ConfigureServices() => AddScoped<IFoo, Foo>();
}
