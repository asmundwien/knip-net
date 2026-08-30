using System;

namespace CatH.H5;

// H5 (PROMOTED — WS5 serialization plugin): a DTO property read/written ONLY by a JSON serializer (via
// reflection over its properties) is invisible to the walker — no source ever reads dto.Name — so the
// property is flagged dead even though the DTO type itself is alive (passed to Serialize). The
// serialization plugin roots the public data members of a DEMONSTRABLY-serialized type, keeping them
// alive. Over-rooting guard (two decoys): a member on a NON-serialized type, and an unrelated dead type,
// STAY flagged — the plugin roots only the serialized type's own data members, never the whole solution.

// Local serializer stand-in, mapped explicitly to System.Text.Json.JsonSerializer by the fixture config.
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
        var dto = new PersonDto();     // DTO TYPE referenced -> alive; its PROPERTY is not read in source
        _ = JsonSerializer.Serialize(dto);

        _ = new NonDto();              // NonDto TYPE referenced -> alive; but it is NEVER serialized, so
                                       // its PlainDead property is not rooted by the plugin (decoy #1).
    }
}

public sealed class PersonDto
{
    // ALIVE (plugin): PersonDto is serialized, so its public data members are rooted — touched only by
    // the serializer's reflection, never read in source.
    public string Name { get; set; } = "";
}

// DECOY #1 — non-serialized plain dead member: NonDto is never serialized (never passed to Serialize),
// so the plugin does NOT root its members. This plain unread property STAYS flagged (over-rooting guard:
// the plugin roots serialized types' members, not every property in the solution).
public sealed class NonDto
{
    public string PlainDead { get; set; } = "";
}

// DECOY #2 — unrelated dead type: never referenced anywhere. STAYS flagged (the plugin roots data
// members of serialized types, never whole unrelated types).
public sealed class UnrelatedType
{
}
