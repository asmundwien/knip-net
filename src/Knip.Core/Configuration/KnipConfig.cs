using System.Text.Json;
using Knip.Core.Plugins;

namespace Knip.Core.Configuration;

/// <summary>
/// Config-as-code for a Knip.NET run. Deserialized from <c>knip.json</c>.
/// Every property carries a sensible default (tuned for ASP.NET Core + common test
/// frameworks) so a zero-config run is useful; the JSON file overrides only what it sets.
/// </summary>
public sealed class KnipConfig
{

    public EntryPointConfig EntryPoints { get; set; } = new();
    public RootConfig Roots { get; set; } = new();
    public IgnoreConfig Ignore { get; set; } = new();

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

    /// <summary>Built-in analysis plugins keyed by their case-sensitive registry id.</summary>
    public Dictionary<string, PluginSettings> Plugins { get; set; } = new(StringComparer.Ordinal);


    /// <summary>Resolve whether a built-in plugin runs: explicit config wins, else its registry default.</summary>
    public bool IsPluginEnabled(PluginDescriptor descriptor) =>
        Plugins.TryGetValue(descriptor.Id, out var settings) && settings.Enabled is { } enabled
            ? enabled
            : descriptor.DefaultEnabled;

    /// <summary>The plugin's own config block, or an empty (disabled) block if it has none.</summary>
    public PluginSettings PluginSettingsFor(string id) =>
        Plugins.TryGetValue(id, out var settings) ? settings : PluginSettings.None;

    /// <summary>Validate programmatically constructed plugin settings against the registry.</summary>
    public IReadOnlyList<string> ValidatePlugins()
    {
        var known = PluginRegistry.All.ToDictionary(plugin => plugin.Id, StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var (id, settings) in Plugins)
        {
            if (!known.TryGetValue(id, out var descriptor))
            {
                errors.Add($"unknown plugin '{id}'");
                continue;
            }

            foreach (var key in settings.Extra.Keys)
                if (!descriptor.SettingKeys.Contains(key))
                    errors.Add($"unknown setting 'plugins.{id}.{key}'");
        }
        return errors;
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static KnipConfig Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new KnipConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<KnipConfig>(json, JsonOptions) ?? new KnipConfig();
    }


    /// <summary>Find the nearest knip.json from a directory through its ancestors.</summary>
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
