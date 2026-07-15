using System;

namespace CatH.H12;

// H12 (G-moat): a MassTransit consumer registered by ASSEMBLY SCANNING (cfg.AddConsumers(assembly))
// is discovered reflectively — never named in source — so the walker flags it dead. (The generic
// AddConsumer<MyConsumer>() form would be a visible GenericName edge; the moat is the scanning form.)
// CORRECT eventual behavior (WS5 MassTransit plugin): scanned consumers should be ALIVE.
// Mitigation today: entryPoints.implementedInterfaces ["CatH.H12.IConsumer<T>"] or a name pattern.
public interface IConsumer<TMessage>
{
    void Consume(TMessage message);
}

public sealed class OrderPlaced { }

// Local stand-in for MassTransit's registration shape. No real framework.
public sealed class BusRegistration
{
    // Reflectively registers every IConsumer<> found in the given marker's assembly.
    public void AddConsumers(Type assemblyMarker) { _ = assemblyMarker; }
}

public sealed class Registration
{
    public void ConfigureServices()
    {
        var bus = new BusRegistration();
        // OrderConsumer is discovered by scanning typeof(Registration)'s assembly — never named here.
        bus.AddConsumers(typeof(Registration));
    }
}

// ALIVE (scanningDi plugin): registered only via AddConsumers assembly scanning. The plugin roots
// types whose SHAPE matches the IConsumer<> marker interface, keeping the consumer, its message type
// (OrderPlaced, via the Consume signature edge) and IConsumer alive.
public sealed class OrderConsumer : IConsumer<OrderPlaced>
{
    public void Consume(OrderPlaced message) { }
}

// DEAD SIBLING / OVER-ROOTING DECOY (honest): not a consumer (implements no IConsumer<>), never
// referenced -> flagged today AND with the scanningDi plugin ON. The H12 ALIVE-with-plugin test (and
// the WS5 over-rooting guard) assert it stays flagged.
public sealed class UnrelatedService
{
    public void Consume(OrderPlaced message) { }
}
