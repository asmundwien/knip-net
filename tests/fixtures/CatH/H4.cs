using System;

namespace CatH.H4;

// H4 (G-moat): assembly-scanning DI (Scrutor .FromAssemblyOf<>), MediatR handlers, AutoMapper
// profiles. The handler is discovered at runtime by REFLECTIVE SCANNING of the assembly for types
// implementing IRequestHandler — it is never named in source, so the walker flags it dead.
// CORRECT eventual behavior (WS5 DI/scanning plugin): the handler should be ALIVE.
// Mitigation today: entryPoints.implementedInterfaces ["CatH.H4.IRequestHandler"] (or baseTypes).
public interface IRequestHandler
{
    void Handle();
}

// Local stand-in for Scrutor's scanning API shape: a generic marker method. No real framework.
public sealed class ServiceCollection
{
    // Registers every IRequestHandler found in the assembly containing TMarker — reflectively.
    public void ScanHandlersFromAssemblyOf<TMarker>() { }
}

public sealed class Registration
{
    public void ConfigureServices()
    {
        var services = new ServiceCollection();
        // TMarker is Registration itself; MyHandler is discovered by scanning, never named here.
        services.ScanHandlersFromAssemblyOf<Registration>();
    }
}

// ALIVE (future): registered only by assembly scanning for IRequestHandler implementers.
public sealed class MyHandler : IRequestHandler
{
    public void Handle() { }
}

// DEAD SIBLING (honest): does NOT implement the scanned interface, never referenced -> flagged.
public sealed class UnrelatedType
{
    public void Handle() { }
}
