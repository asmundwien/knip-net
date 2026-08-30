using System.Text.Json;
using System.Text.Json.Serialization;
using Knip.Core.Plugins;

namespace Knip.Core.Configuration;

/// <summary>
/// Config-as-code for a Knip.NET run. Deserialized from <c>knip.json</c>.
/// Every property carries a sensible default (tuned for ASP.NET Core + common test
/// frameworks) so a zero-config run is useful; the JSON file overrides only what it sets.
/// </summary>
public sealed class KnipConfig
{
    /// <summary>Path to the .sln/.slnx/.csproj to analyze. CLI <c>--solution</c> overrides this.</summary>
    public string? Solution { get; set; }

    public EntryPointConfig EntryPoints { get; set; } = new();
    public RootConfig Roots { get; set; } = new();
    public IgnoreConfig Ignore { get; set; } = new();
    public OutputConfig Output { get; set; } = new();

    /// <summary>
    /// (WS7) Production-mode analysis. When true, roots seeded from TEST projects are two-color-demoted:
    /// production code reachable ONLY through test roots is reported as
    /// <see cref="Model.FindingKind.OnlyUsedByTests"/> (the largest deletable unit — a dead feature and
    /// its whole test suite). OFF by default (K1: default semantics keep test-only production code alive).
    /// Enabled via <c>--production</c> on the CLI or this key in knip.json.
    /// </summary>
    public bool Production { get; set; }

    /// <summary>
    /// (WS7) Project-name globs that classify a project as a TEST project (highest-priority signal, K7:
    /// first match wins). When set for a project, it overrides both the referenced-test-framework-assembly
    /// signal and the name-glob fallback. Empty by default (auto-detection then applies).
    /// </summary>
    public List<string> TestProjects { get; set; } = [];

    /// <summary>
    /// Built-in analysis plugins, keyed by camelCase plugin id (e.g. "reflection"). Each block sets
    /// <c>enabled</c> plus optional per-plugin settings. A plugin absent here uses its default-enabled
    /// state (see <see cref="PluginRegistry"/>). Unknown ids / unknown per-plugin keys produce a
    /// visible warning (see <see cref="ValidatePlugins"/>) — they never silently no-op.
    /// </summary>
    public Dictionary<string, PluginSettings> Plugins { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The knip.json path this config was loaded from (or null for a zero-config/default run). Set by
    /// <see cref="Load"/>; consumed by <see cref="ValidateKeys(string?)"/> so the analyzer can emit
    /// unknown-key warnings (WS8c / L7) without threading the path separately. Not serialized.
    /// </summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    /// <summary>Resolve whether a built-in plugin runs: explicit config wins, else its registry default.</summary>
    public bool IsPluginEnabled(PluginDescriptor descriptor) =>
        Plugins.TryGetValue(descriptor.Id, out var settings) && settings.Enabled is { } enabled
            ? enabled
            : descriptor.DefaultEnabled;

    /// <summary>The plugin's own config block, or an empty (disabled) block if it has none.</summary>
    public PluginSettings PluginSettingsFor(string id) =>
        Plugins.TryGetValue(id, out var settings) ? settings : PluginSettings.None;

    /// <summary>
    /// Warn on config that would otherwise silently no-op: an unknown plugin id (e.g. a typo like
    /// <c>reflectoin</c>) or an unknown per-plugin setting key (e.g. <c>enabldd</c>). Emitted through
    /// the LoadDiagnostics channel so it is VISIBLE. Returns the warning strings (empty when clean).
    /// </summary>
    public IReadOnlyList<string> ValidatePlugins()
    {
        var known = PluginRegistry.All.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (var (id, settings) in Plugins)
        {
            if (!known.TryGetValue(id, out var descriptor))
            {
                warnings.Add(
                    $"unknown plugin '{id}' in knip.json 'plugins' — no such built-in plugin. " +
                    $"Known plugins: {string.Join(", ", known.Keys.OrderBy(k => k, StringComparer.Ordinal))}.");
                continue;
            }

            foreach (var key in settings.Extra.Keys)
                if (!descriptor.SettingKeys.Contains(key))
                    warnings.Add(
                        $"unknown setting 'plugins.{id}.{key}' in knip.json — the '{id}' plugin does not " +
                        $"recognize this key (it will be ignored).");
        }

        return warnings;
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>
    /// The known key TREE of knip.json (camelCase), generalizing the <see cref="ValidatePlugins"/>
    /// unknown-key pattern to EVERY object in the file (WS8c / L7). A leaf value of <c>null</c> means
    /// "known key, do not descend" (scalars, arrays, and the dynamic <c>plugins.&lt;id&gt;</c> map whose
    /// keys <see cref="ValidatePlugins"/> validates separately). Used ONLY for unknown-key warnings;
    /// value-shape validation stays with System.Text.Json / the published JSON Schema.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, object?> KnownKeys =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["$schema"] = null,
            ["solution"] = null,
            ["production"] = null,
            ["testProjects"] = null,
            ["entryPoints"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["symbolNames"] = null,
                ["attributes"] = null,
                ["baseTypes"] = null,
                ["implementedInterfaces"] = null,
                ["namePatterns"] = null,
            },
            ["roots"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["treatAllPublicAsUsed"] = null,
                ["publicApiProjects"] = null,
            },
            ["ignore"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["files"] = null,
                ["symbols"] = null,
                ["namespaces"] = null,
                ["projects"] = null,
            },
            ["output"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["format"] = null,
            },
            // plugins.<id> keys are dynamic (plugin ids) — validated by ValidatePlugins, not descended here.
            ["plugins"] = null,
        };

    public static KnipConfig Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new KnipConfig();

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<KnipConfig>(json, JsonOptions) ?? new KnipConfig();
        config.SourcePath = path;
        return config;
    }

    /// <summary>(WS8c / L7) Unknown-key warnings for the file this config was loaded from.</summary>
    public IReadOnlyList<string> ValidateKeys() => ValidateKeys(SourcePath);

    /// <summary>
    /// (WS8c / L7) Warn on ANY unknown key in knip.json — top-level AND nested — naming the key path
    /// (e.g. <c>roots.treatAllPubic</c>). Generalizes <see cref="ValidatePlugins"/> to the whole file:
    /// one warning per unknown key, then analysis proceeds (exit unchanged). Routed through the same
    /// LoadDiagnostics channel. Returns empty for a missing/empty/unparseable file (STJ Load surfaces
    /// real parse errors separately). Value-SHAPE mismatches are not this method's job.
    /// </summary>
    public static IReadOnlyList<string> ValidateKeys(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return [];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException)
        {
            return []; // malformed JSON is reported by Load; nothing structural to diff.
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];
            var warnings = new List<string>();
            WalkKeys(doc.RootElement, KnownKeys, prefix: "", warnings);
            return warnings;
        }
    }

    private static void WalkKeys(
        JsonElement element,
        IReadOnlyDictionary<string, object?>? known,
        string prefix,
        List<string> warnings)
    {
        foreach (var property in element.EnumerateObject())
        {
            var path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            if (known is null || !known.TryGetValue(property.Name, out var child))
            {
                var siblings = known is null
                    ? ""
                    : $" Known keys{(prefix.Length == 0 ? "" : $" under '{prefix}'")}: " +
                      $"{string.Join(", ", known.Keys.OrderBy(k => k, StringComparer.Ordinal))}.";
                warnings.Add($"unknown key '{path}' in knip.json — it will be ignored.{siblings}");
                continue;
            }

            // Known key with a child schema and an object value → recurse to catch nested unknown keys.
            if (child is IReadOnlyDictionary<string, object?> childKnown
                && property.Value.ValueKind == JsonValueKind.Object)
                WalkKeys(property.Value, childKnown, path, warnings);
        }
    }

    /// <summary>Locate a knip.json next to the solution or in the current directory.</summary>
    public static string? Discover(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "knip.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}

/// <summary>
/// Symbols the framework calls that the source never references by name. These seed the
/// reachability graph — anything reachable from an entry point is considered "used".
/// </summary>
public sealed class EntryPointConfig
{
    /// <summary>Explicit method/type names that are always roots.</summary>
    public List<string> SymbolNames { get; set; } = [];

    /// <summary>
    /// Attribute identities that mark a member as an entry point. Built-ins use
    /// <c>Assembly::Namespace.Type</c>; configured namespace-qualified or simple names remain explicit
    /// aliases for source-only fixtures and custom frameworks. The <c>Attribute</c> suffix is optional.
    /// </summary>
    public List<string> Attributes { get; set; } =
    [
        // xUnit v2 and v3
        "xunit.core::Xunit.FactAttribute", "xunit.core::Xunit.TheoryAttribute",
        "xunit.v3.core::Xunit.FactAttribute", "xunit.v3.core::Xunit.TheoryAttribute",
        // MSTest
        "Microsoft.VisualStudio.TestPlatform.TestFramework::Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
        "Microsoft.VisualStudio.TestPlatform.TestFramework::Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute",
        "Microsoft.VisualStudio.TestPlatform.TestFramework::Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute",
        "Microsoft.VisualStudio.TestPlatform.TestFramework::Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanupAttribute",
        "Microsoft.VisualStudio.TestPlatform.TestFramework::Microsoft.VisualStudio.TestTools.UnitTesting.ClassInitializeAttribute",
        "Microsoft.VisualStudio.TestPlatform.TestFramework::Microsoft.VisualStudio.TestTools.UnitTesting.ClassCleanupAttribute",
        "Microsoft.VisualStudio.TestPlatform.TestFramework::Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyInitializeAttribute",
        "Microsoft.VisualStudio.TestPlatform.TestFramework::Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyCleanupAttribute",
        // NUnit
        "nunit.framework::NUnit.Framework.TestAttribute",
        "nunit.framework::NUnit.Framework.TestCaseAttribute",
        "nunit.framework::NUnit.Framework.SetUpAttribute",
        "nunit.framework::NUnit.Framework.TearDownAttribute",
        "nunit.framework::NUnit.Framework.OneTimeSetUpAttribute",
        "nunit.framework::NUnit.Framework.OneTimeTearDownAttribute",
        // BenchmarkDotNet
        "BenchmarkDotNet.Annotations::BenchmarkDotNet.Attributes.BenchmarkAttribute",
        "BenchmarkDotNet.Annotations::BenchmarkDotNet.Attributes.GlobalSetupAttribute",
    ];

    /// <summary>
    /// Custom base classes whose subtypes and externally visible members are roots. Built-in framework
    /// entry types are handled by convention-specific plugins instead of this broad escape hatch.
    /// </summary>
    public List<string> BaseTypes { get; set; } = [];

    /// <summary>
    /// Custom interfaces whose implementers and externally visible members are roots. Built-in hosted
    /// services are handled by convention-specific plugins instead of this broad escape hatch.
    /// </summary>
    public List<string> ImplementedInterfaces { get; set; } = [];

    /// <summary>Custom type-name globs whose matching types and externally visible members are roots.</summary>
    public List<string> NamePatterns { get; set; } = [];
}

public sealed class RootConfig
{
    /// <summary>Treat every externally-visible (public/protected) symbol as used. Good for pure libraries.</summary>
    public bool TreatAllPublicAsUsed { get; set; }

    /// <summary>Project-name globs whose public API is the "used" surface (library contracts consumed externally).</summary>
    public List<string> PublicApiProjects { get; set; } = [];
}

public sealed class IgnoreConfig
{
    /// <summary>File-path globs to skip entirely (generated code, migrations, build output).</summary>
    public List<string> Files { get; set; } =
    [
        "**/*.g.cs", "**/*.Designer.cs", "**/*.AssemblyInfo.cs", "**/*.AssemblyAttributes.cs",
        "**/GlobalUsings.g.cs", "**/obj/**", "**/bin/**", "**/Migrations/**",
    ];

    /// <summary>Fully-qualified symbol-name globs to never report (e.g. reflection/serialization targets).</summary>
    public List<string> Symbols { get; set; } = [];

    /// <summary>Namespace globs to never report.</summary>
    public List<string> Namespaces { get; set; } = [];

    /// <summary>Project-name globs to skip loading (e.g. throwaway spikes).</summary>
    public List<string> Projects { get; set; } = [];
}

public sealed class OutputConfig
{
    /// <summary>console | json | sarif</summary>
    public string Format { get; set; } = "console";
}
