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

// ALIVE (scanningDi plugin): registered only by assembly scanning for IRequestHandler implementers.
// The plugin roots types whose SHAPE matches a scanned marker interface (IRequestHandler), keeping the
// concrete handler and its interface alive.
public sealed class MyHandler : IRequestHandler
{
    public void Handle() { }
}

// DEAD SIBLING / OVER-ROOTING DECOY (honest): does NOT implement the scanned interface, never
// referenced -> flagged today AND with the scanningDi plugin ON. A blanket plugin that rooted every
// type near a scan call would wrongly keep this alive; the H4 ALIVE-with-plugin test (and the WS5
// over-rooting guard) assert it stays flagged.
public sealed class UnrelatedType
{
    public void Handle() { }
}
