using System.Text.Json;
using System.Text.Json.Serialization;

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
        "Fact", "Theory", "TestMethod", "Test", "TestCase", "SetUp", "TearDown",
        "Benchmark", "GlobalSetup",
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
