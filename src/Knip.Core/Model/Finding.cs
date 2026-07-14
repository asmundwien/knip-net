namespace Knip.Core.Model;

public enum FindingKind
{
    UnusedType,
    UnusedMethod,
    UnusedProperty,
    UnusedField,
    UnusedEvent,
}

/// <summary>A single piece of unreferenced code, with enough location info to jump to it.</summary>
public sealed record Finding(
    FindingKind Kind,
    string Symbol,        // fully-qualified display name
    string SymbolKind,    // "class", "method", "property", ...
    string Accessibility, // "public", "internal", "private", ...
    string Project,
    string FilePath,
    int Line,             // 1-based
    int Column);          // 1-based
