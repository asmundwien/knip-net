using System;

namespace CatH.H3;

// H3 (CONTRACT — must be GREEN): non-generic DI registration AddScoped(typeof(IFoo), typeof(Foo)).
// typeof(Foo) yields an IdentifierName node that GetSymbolInfo resolves to Foo, so the walker records
// a real edge — Foo is ALIVE via the typeof edge WITHOUT any plugin. This is the visible half of DI:
// whatever appears inside typeof(...) is seen. Only assembly SCANNING (H4) is the moat.
public interface IFoo
{
    void Do();
}

// ALIVE: reached via typeof(Foo) in the registration below.
public sealed class Foo : IFoo
{
    public void Do() { }
}

// DEAD SIBLING (honest): a concrete type never mentioned in any typeof -> flagged.
public sealed class UnreferencedFoo : IFoo
{
    public void Do() { }
}

// Local stand-in for the DI container shape: signature AddScoped(Type, Type). No real framework.
public sealed class ServiceCollection
{
    public void AddScoped(Type service, Type implementation)
    {
        _ = service;
        _ = implementation;
    }
}

public sealed class Registration
{
    public void ConfigureServices()
    {
        var services = new ServiceCollection();
        // typeof(Foo) is the ONLY reference to Foo — and it is enough (IdentifierName edge).
        services.AddScoped(typeof(IFoo), typeof(Foo));
    }
}
