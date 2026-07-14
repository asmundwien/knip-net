using System;

namespace CatH.H5;

// H5 (G-moat): a DTO property read/written ONLY by a JSON serializer (via reflection over its
// properties) is invisible to the walker — no source ever reads dto.Name — so the property is
// flagged dead even though the DTO type itself is alive (passed to Serialize).
// CORRECT eventual behavior (WS5 serializer plugin): serialized DTO properties should be ALIVE.
// Mitigation today: ignore.symbols on DTO namespaces, e.g. ignore.namespaces ["CatH.H5.Dto*"].

// Local stand-in for a serializer shape: accepts an object, reflects over its properties. No framework.
public static class JsonSerializer
{
    public static string Serialize(object value)
    {
        _ = value; // in reality: reflects over value's public properties (Name) — invisible to walker
        return "{}";
    }
}

public sealed class Endpoint
{
    public void ConfigureServices()
    {
        var dto = new PersonDto(); // DTO TYPE referenced -> alive; its PROPERTY is not
        _ = JsonSerializer.Serialize(dto);
    }
}

public sealed class PersonDto
{
    // ALIVE (future): touched only by the serializer's reflection, never read in source.
    public string Name { get; set; } = "";

    // DEAD SIBLING (honest): a property no serializer and no code ever touches -> flagged.
    public string InternalScratch { get; set; } = "";
}
