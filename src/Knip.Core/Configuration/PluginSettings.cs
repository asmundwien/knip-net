using System.Text.Json;
using System.Text.Json.Serialization;

namespace Knip.Core.Configuration;

/// <summary>
/// One plugin's config block from knip.json (<c>plugins.&lt;id&gt;</c>). <see cref="Enabled"/> turns the
/// built-in plugin on/off; any other keys are a free-form, per-plugin settings bag captured raw so
/// each plugin validates and reads its own sub-keys. Exposed read-only to plugins via PluginContext.
/// </summary>
public sealed class PluginSettings
{
    /// <summary>Whether the plugin runs. If omitted in JSON, the built-in default-enabled set decides.</summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Every key on the plugin's config object OTHER than <c>enabled</c>, captured raw. Populated by
    /// System.Text.Json's [JsonExtensionData]; consulted for per-plugin settings and unknown-key warnings.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Empty, disabled-by-default settings — the value handed to a plugin with no config block.</summary>
    public static PluginSettings None { get; } = new();
}
