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

    public static KnipConfig Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new KnipConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<KnipConfig>(json, JsonOptions) ?? new KnipConfig();
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
    /// <summary>Method/type names that are always roots (e.g. Main, Startup conventions).</summary>
    public List<string> SymbolNames { get; set; } = ["Main", "ConfigureServices", "Configure", "ConfigureContainer"];

    /// <summary>Attribute names (with or without the "Attribute" suffix) that mark a member as an entry point.</summary>
    public List<string> Attributes { get; set; } =
    [
        // xUnit
        "Fact", "Theory",
        // MSTest (test + lifecycle hooks — invoked by the framework, never by name in source)
        "TestMethod", "DataTestMethod",
        "TestInitialize", "TestCleanup",
        "ClassInitialize", "ClassCleanup",
        "AssemblyInitialize", "AssemblyCleanup",
        // NUnit (test + one-time and per-test lifecycle hooks)
        "Test", "TestCase", "SetUp", "TearDown", "OneTimeSetUp", "OneTimeTearDown",
        // BenchmarkDotNet
        "Benchmark", "GlobalSetup",
        // ASP.NET Core routing
        "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "Route",
    ];

    /// <summary>Fully-qualified base classes whose subtypes (and their public members) are roots.</summary>
    public List<string> BaseTypes { get; set; } =
    [
        "Microsoft.AspNetCore.Mvc.ControllerBase",
        "Microsoft.AspNetCore.Mvc.Controller",
        "Microsoft.AspNetCore.Components.ComponentBase",
        "Microsoft.AspNetCore.SignalR.Hub",
        "Microsoft.AspNetCore.Mvc.RazorPages.PageModel",
    ];

    /// <summary>Fully-qualified interfaces whose implementers (and their public members) are roots.</summary>
    public List<string> ImplementedInterfaces { get; set; } =
    [
        "Microsoft.Extensions.Hosting.IHostedService",
        "Microsoft.Extensions.Hosting.BackgroundService",
    ];

    /// <summary>Type-name globs whose matching types (and public members) are roots, e.g. "*Controller".</summary>
    public List<string> NamePatterns { get; set; } = ["*Controller"];
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
