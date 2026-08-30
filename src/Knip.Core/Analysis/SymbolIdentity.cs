using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

internal static class SymbolIdentity
{
    private const string AssemblySeparator = "::";

    public static bool MatchesType(INamedTypeSymbol? symbol, string configuredName) =>
        Matches(symbol, configuredName, allowAttributeSuffix: false);

    public static bool MatchesAttribute(INamedTypeSymbol? symbol, string configuredName) =>
        Matches(symbol, configuredName, allowAttributeSuffix: true);

    private static bool Matches(
        INamedTypeSymbol? symbol,
        string configuredName,
        bool allowAttributeSuffix)
    {
        if (symbol is null || configuredName.Length == 0) return false;

        var definition = symbol.OriginalDefinition;
        var separator = configuredName.IndexOf(AssemblySeparator, StringComparison.Ordinal);
        var expectedType = separator < 0
            ? configuredName
            : configuredName[(separator + AssemblySeparator.Length)..];

        if (!MatchesName(definition, expectedType, allowAttributeSuffix)) return false;
        if (separator < 0) return true;

        var expectedAssembly = configuredName[..separator];
        return string.Equals(
            definition.ContainingAssembly?.Identity.Name,
            expectedAssembly,
            StringComparison.Ordinal);
    }

    private static string QualifiedName(INamedTypeSymbol symbol)
    {
        var definition = symbol.OriginalDefinition;
        var typeName = definition.Name;

        for (var containing = definition.ContainingType; containing is not null; containing = containing.ContainingType)
            typeName = containing.Name + "." + typeName;

        var ns = definition.ContainingNamespace?.ToDisplayString();
        return string.IsNullOrEmpty(ns) ? typeName : ns + "." + typeName;
    }

    private static bool MatchesName(
        INamedTypeSymbol symbol,
        string expectedType,
        bool allowAttributeSuffix)
    {
        var actualQualified = QualifiedName(symbol);
        var expectedIsQualified = expectedType.IndexOf('.') >= 0;
        var actual = expectedIsQualified ? actualQualified : symbol.Name;

        if (string.Equals(actual, expectedType, StringComparison.Ordinal)) return true;
        if (!allowAttributeSuffix) return false;

        return string.Equals(
            TrimAttributeSuffix(actual),
            TrimAttributeSuffix(expectedType),
            StringComparison.Ordinal);
    }

    private static string TrimAttributeSuffix(string name) =>
        name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name[..^"Attribute".Length]
            : name;
}
