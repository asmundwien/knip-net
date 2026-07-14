namespace Knip.Core.Model;

public enum FindingKind
{
    UnusedType,
    UnusedMethod,
    UnusedProperty,
    UnusedField,
    UnusedEvent,

    /// <summary>A &lt;ProjectReference&gt; whose referencing project touches no symbol in the referenced assembly.</summary>
    UnusedProjectReference,
}

/// <summary>
/// A single piece of unreferenced code, with enough location info to jump to it.
/// For <see cref="FindingKind.UnusedProjectReference"/> the referencing project is <see cref="Project"/>,
/// the referenced project is <see cref="ReferencedProject"/>, <see cref="Symbol"/> is the referenced
/// project name, <see cref="FilePath"/> points at the referencing .csproj, and Line/Column are 0.
/// </summary>
public sealed record Finding(
    FindingKind Kind,
    string Symbol,        // fully-qualified display name (or referenced project name)
    string SymbolKind,    // "class", "method", "property", "project reference", ...
    string Accessibility, // "public", "internal", "private", ...
    string Project,
    string FilePath,
    int Line,             // 1-based (0 when not applicable, e.g. project references)
    int Column,           // 1-based (0 when not applicable)
    string? ReferencedProject = null); // set only for UnusedProjectReference
