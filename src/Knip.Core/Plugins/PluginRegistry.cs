using Knip.Core.Plugins.BuiltIn;

namespace Knip.Core.Plugins;

/// <summary>
/// Metadata for one built-in plugin: its id, how to build it, whether it ships enabled, and the
/// per-plugin setting keys it understands (so a typo like <c>plugins.reflection.enabldd</c> warns
/// instead of silently no-opping). NO external assembly loading — built-in only (invariant #9).
/// </summary>
public sealed class PluginDescriptor
{
    public PluginDescriptor(string id, bool defaultEnabled, Func<IKnipPlugin> factory, params string[] settingKeys)
    {
        Id = id;
        DefaultEnabled = defaultEnabled;
        Factory = factory;
        SettingKeys = new HashSet<string>(settingKeys, StringComparer.Ordinal);
    }

    /// <summary>camelCase id, matching the <c>plugins.&lt;id&gt;</c> key in knip.json.</summary>
    public string Id { get; }

    /// <summary>Whether the plugin runs when knip.json does not mention it. The v1 default-on set.</summary>
    public bool DefaultEnabled { get; }

    /// <summary>Builds a fresh plugin instance.</summary>
    public Func<IKnipPlugin> Factory { get; }

    /// <summary>Per-plugin setting keys this plugin understands (besides <c>enabled</c>).</summary>
    public HashSet<string> SettingKeys { get; }
}

/// <summary>
/// The static, built-in plugin registry. Order here is the order plugins run in. The default-on set
/// for v1 is <c>{reflection, scanningDi}</c>. blazorParameter/serialization default OFF.
/// </summary>
public static class PluginRegistry
{
    public static IReadOnlyList<PluginDescriptor> All { get; } =
    [
        new("reflection", defaultEnabled: true, () => new ReflectionPlugin()),
        new("scanningDi", defaultEnabled: true, () => new ScanningDiPlugin()),
        // blazorParameter/serialization — land in later tasks; register default OFF.
    ];

    /// <summary>The ids that run under a default (<c>new KnipConfig()</c>) configuration.</summary>
    public static IReadOnlyList<string> DefaultEnabledIds { get; } =
        All.Where(d => d.DefaultEnabled).Select(d => d.Id).ToList();
}
