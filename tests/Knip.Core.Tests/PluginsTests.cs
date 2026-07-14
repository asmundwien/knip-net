using Knip.Core.Configuration;
using Knip.Core.Plugins;
using Xunit;

namespace Knip.Core.Tests;

/// <summary>
/// WS5 — the plugin seam and the first built-in plugin (<c>reflection</c>). All rows are Contract.
///
/// Covers three sign-off conditions:
///   • The reflection differential (flagged WITHOUT the plugin, alive WITH it) — the same fixtures the
///     CatH battery promotes (H1/H2) — PLUS an over-rooting guard: an unrelated dead symbol that must
///     STAY FLAGGED with the plugin ON (the plugin-OFF run alone is not sufficient).
///   • The default-enabled set (F8-style): pins which plugins run under a default KnipConfig.
///   • Config warnings: an unknown plugin id and an unknown per-plugin key each surface a VISIBLE
///     diagnostic — they never silently no-op.
/// </summary>
[Collection(MsBuildCollection.Name)]
public sealed class PluginsTests
{
    private const string Category = "CatH";

    private static KnipConfig WithReflection(bool enabled) => new()
    {
        Plugins = { ["reflection"] = new PluginSettings { Enabled = enabled } },
    };

    private static Task<IReadOnlySet<string>> FindingsIn(string ns, KnipConfig config) =>
        FixtureRunner.FindingSymbolsInAsync(Category, ns, config);

    // ── reflection differential + over-rooting guard: H1 (GetMethod("X").Invoke) ──────────────
    [Fact]
    public async Task Reflection_getmethod_string_keeps_member_alive_H1()
    {
        // Plugin OFF: Handle() is a false positive (reached only via reflection) → flagged.
        var off = await FindingsIn("CatH.H1", WithReflection(false));
        Assert.Contains("CatH.H1.Service.Handle()", off);

        // Plugin ON: the plugin roots the reflected member → Handle() alive → NOT reported…
        var on = await FindingsIn("CatH.H1", WithReflection(true));
        Assert.DoesNotContain("CatH.H1.Service.Handle()", on);
        // …but the unrelated dead sibling STAYS FLAGGED (over-rooting guard: the plugin roots only the
        // reflected member on the reflected type, not every member of it).
        Assert.Contains("CatH.H1.Service.NeverCalled()", on);
    }

    // ── reflection differential + over-rooting guard: H2 (Type.GetType("Ns.Foo")) ─────────────
    [Fact]
    public async Task Reflection_typegettype_string_keeps_type_alive_H2()
    {
        // Plugin OFF: the string-named type is a false positive → flagged.
        var off = await FindingsIn("CatH.H2", WithReflection(false));
        Assert.Contains("CatH.H2.Plugin", off);

        // Plugin ON: Type.GetType("CatH.H2.Plugin") roots the named type → alive → NOT reported…
        var on = await FindingsIn("CatH.H2", WithReflection(true));
        Assert.DoesNotContain("CatH.H2.Plugin", on);
        // …but the unrelated dead type STAYS FLAGGED (plugin resolves EXACTLY the string it sees).
        Assert.Contains("CatH.H2.UnusedPlugin", on);
    }

    // ── default-enabled set (F8-style): pin which plugins run under a default config ──────────
    [Fact]
    public void DefaultEnabledSet_is_reflection_only_for_v1()
    {
        // The shipped default-on set. Adding scanningDi later just flips its DefaultEnabled → it appears
        // here. This pins the seam's default so a regression (accidentally enabling/disabling) is caught.
        Assert.Equal(new[] { "reflection" }, PluginRegistry.DefaultEnabledIds);

        var config = new KnipConfig();
        Assert.True(config.IsPluginEnabled(Descriptor("reflection")), "reflection must default ON");
    }

    [Fact]
    public async Task DefaultConfig_runs_reflection_so_H2_type_is_alive()
    {
        // RED-FLIP through config, analogous to F8: default config (reflection ON by default) keeps the
        // reflected type alive; explicitly disabling reflection flips it back to flagged.
        var byDefault = await FindingsIn("CatH.H2", new KnipConfig());
        Assert.DoesNotContain("CatH.H2.Plugin", byDefault);

        var disabled = await FindingsIn("CatH.H2", WithReflection(false));
        Assert.Contains("CatH.H2.Plugin", disabled);
    }

    // ── config warnings: unknown plugin id ────────────────────────────────────────────────────
    [Fact]
    public async Task UnknownPluginId_emits_visible_warning()
    {
        var config = new KnipConfig
        {
            Plugins = { ["reflectoin"] = new PluginSettings { Enabled = true } }, // typo of "reflection"
        };
        var result = await FixtureRunner.RunAsync(Category, config);
        Assert.Contains(result.LoadDiagnostics, d => d.Contains("unknown plugin 'reflectoin'"));
    }

    // ── config warnings: unknown per-plugin key ───────────────────────────────────────────────
    [Fact]
    public async Task UnknownPerPluginKey_emits_visible_warning()
    {
        // A recognized plugin id, but a typo'd setting key must not silently no-op.
        var settings = new PluginSettings { Enabled = true };
        settings.Extra["enabldd"] = System.Text.Json.JsonSerializer.SerializeToElement(true);
        var config = new KnipConfig { Plugins = { ["reflection"] = settings } };

        var result = await FixtureRunner.RunAsync(Category, config);
        Assert.Contains(result.LoadDiagnostics, d => d.Contains("plugins.reflection.enabldd"));
    }

    // A clean, fully-specified config produces NO plugin warnings (anti-vacuous-green for the above).
    [Fact]
    public void ValidatePlugins_clean_config_has_no_warnings()
    {
        var config = new KnipConfig
        {
            Plugins = { ["reflection"] = new PluginSettings { Enabled = true } },
        };
        Assert.Empty(config.ValidatePlugins());
    }

    private static PluginDescriptor Descriptor(string id) =>
        PluginRegistry.All.Single(d => d.Id == id);
}
