namespace CatL.HazardShapes;

// The fixture uses framework-provided serializer and configuration symbols so namespace and defining-
// assembly checks are exercised without a package restore.

// ── USAGE-shaped serialization: UserDto is alive (deserialized in Startup) but its data members are never
//    read in source — the field shape. Data members are tagged serializationShaped; the DEAD METHOD is not. ─
public sealed class UserDto
{
    public string Name { get; set; } = "";   // DEAD data member -> serializationShaped (usage)
    public int Age { get; set; }             // DEAD data member -> serializationShaped (usage)
    public string Describe() => "user";      // DEAD METHOD -> reported, NOT serializationShaped (sibling)
}

// ── OVER-TAG GUARD (serialization usage): PlainPoco is alive but NEVER serialized — its dead member carries
//    NO serialization hazard, proving the detector doesn't blanket-tag every alive type's members. ─────────
public sealed class PlainPoco
{
    public string Label { get; set; } = "";  // DEAD, not serialized -> NO serializationShaped (sibling)
}

// ── ATTRIBUTE-shaped serialization (type-level [Serializable]). Version is INTERNAL (no publicApi hazard),
//    so it isolates the proof that a serialization hazard ALONE demotes the finding to low. ───────────────
[System.Serializable]
public sealed class LegacyDto
{
    internal int Version;                     // DEAD internal field -> serializationShaped (type attr); low
    public string Note { get; set; } = "";   // DEAD -> serializationShaped (type attr)
}

// ── ATTRIBUTE-shaped serialization (member-level [JsonProperty]) + within-type sibling. Both members are
//    INTERNAL so the serialization hazard, not publicApi, is the graded signal. ───────────────────────────
public sealed class TaggedDto
{
    [System.Text.Json.Serialization.JsonPropertyName("marked")] internal string Marked { get; set; } = "";  // DEAD -> serializationShaped (member attr)
    internal string Plain { get; set; } = "";                  // DEAD, no attr, type not serialized -> NOT tagged (sibling)
}

// ── CONFIG-bound: DbOptions is bound via ConfigurationBinder.Get<T>; its dead public properties are tagged
//    configBoundType. ─────────────────────────────────────────────────────────────────────────────────────
public sealed class DbOptions
{
    public string ConnectionString { get; set; } = "";  // DEAD -> configBoundType
    public int Timeout { get; set; }                     // DEAD -> configBoundType
}

// Binding through a generic helper must retain the concrete target type at its closed call site.
public sealed class HelperBoundOptions
{
    public string Endpoint { get; set; } = "";  // DEAD -> configBoundType
}

public static class OptionsFactory
{
    public static T Read<T>(Microsoft.Extensions.Configuration.IConfiguration config) =>
        Microsoft.Extensions.Configuration.ConfigurationBinder.Get<T>(config)!;
}

public class SharedOptions
{
    public string Region { get; set; } = "";  // DEAD -> configBoundType through bound derived type
}

public sealed class DerivedBoundOptions : SharedOptions
{
    public string Service { get; set; } = "";  // DEAD -> configBoundType
}

// ── OVER-TAG GUARD (config): PlainSettings is alive but NEVER bound — no configBoundType hazard. ──────────
public sealed class PlainSettings
{
    public string Value { get; set; } = "";  // DEAD, not bound -> NO configBoundType (sibling)
}

// ── Root: ConfigureServices is a default entry-point symbol name (like D8/D10). Keeps the DTO TYPES alive
//    and triggers the serializer/config detection calls, WITHOUT reading any data member. ─────────────────
public sealed class Startup
{
    public void ConfigureServices()
    {
        System.Console.WriteLine(System.Text.Json.JsonSerializer.Deserialize<UserDto>("{}"));

        Microsoft.Extensions.Configuration.IConfiguration config = null!;
        System.Console.WriteLine(Microsoft.Extensions.Configuration.ConfigurationBinder.Get<DbOptions>(config));
        System.Console.WriteLine(OptionsFactory.Read<HelperBoundOptions>(config));
        System.Console.WriteLine(Microsoft.Extensions.Configuration.ConfigurationBinder.Get<DerivedBoundOptions>(config));

        System.Console.WriteLine(new LegacyDto());
        System.Console.WriteLine(new TaggedDto());
        System.Console.WriteLine(new PlainPoco());
        System.Console.WriteLine(new PlainSettings());
    }
}
