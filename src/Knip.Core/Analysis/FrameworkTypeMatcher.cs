using System.Text.Json;
using Knip.Core.Configuration;
using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

internal sealed class FrameworkTypeMatcher
{
    public const string AliasesSettingKey = "aliases";

    private readonly Dictionary<string, string[]> _aliases;

    public FrameworkTypeMatcher(PluginSettings settings)
    {
        _aliases = ReadAliases(settings);
    }

    public bool Matches(INamedTypeSymbol? symbol, string canonicalIdentity)
    {
        if (SymbolIdentity.MatchesType(symbol, canonicalIdentity)) return true;
        if (symbol is null) return false;

        var canonicalName = TypeName(canonicalIdentity);
        return _aliases.TryGetValue(canonicalName, out var aliases)
            && aliases.Any(alias => SymbolIdentity.MatchesType(symbol, alias));
    }

    public bool MatchesAttribute(INamedTypeSymbol? symbol, string canonicalIdentity)
    {
        if (SymbolIdentity.MatchesAttribute(symbol, canonicalIdentity)) return true;
        if (symbol is null) return false;

        var canonicalName = TypeName(canonicalIdentity);
        return _aliases.TryGetValue(canonicalName, out var aliases)
            && aliases.Any(alias => SymbolIdentity.MatchesAttribute(symbol, alias));
    }

    private static Dictionary<string, string[]> ReadAliases(PluginSettings settings)
    {
        var aliases = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!settings.Extra.TryGetValue(AliasesSettingKey, out var element)
            || element.ValueKind != JsonValueKind.Object)
            return aliases;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array) continue;

            var values = property.Value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray();
            if (values.Length > 0)
                aliases[property.Name] = values;
        }

        return aliases;
    }

    private static string TypeName(string identity)
    {
        var separator = identity.IndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? identity : identity[(separator + 2)..];
    }
}
