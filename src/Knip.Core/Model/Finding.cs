namespace Knip.Core.Model;

public enum FindingKind
{
    UnusedType,
    UnusedMethod,
    UnusedProperty,
    UnusedField,
    UnusedEvent,

    /// <summary>An individual enum member (named constant) never referenced, in an otherwise-live enum.</summary>
    UnusedEnumMember,

    /// <summary>A &lt;ProjectReference&gt; whose referencing project touches no symbol in the referenced assembly.</summary>
    UnusedProjectReference,

    /// <summary>
    /// A &lt;PackageReference&gt; none of whose delivered assemblies are touched by any symbol in the
    /// referencing project. Build-only / analyzer / transitive-only / implicit-Using packages are still
    /// EMITTED (REVISED §3.8) with a hazard + low confidence, never dropped.
    /// </summary>
    UnusedPackageReference,

    /// <summary>
    /// (WS7 production mode) A production symbol reachable ONLY via test roots — dead once the tests are
    /// removed. A distinct kind because the remediation differs: "delete the code AND its tests"
    /// (<see cref="Remediation.DeleteCodeAndTests"/>). The referencing test symbols are carried in
    /// <see cref="Finding.TestReferrers"/> so the deletion unit is visible (K3). Emitted only in
    /// production mode (<c>--production</c> / <c>testProjects</c>); default mode keeps these alive (K1).
    /// </summary>
    OnlyUsedByTests,
}

/// <summary>
/// How safe autonomous deletion of a finding is (WS8 §4). Every finding ships <see cref="High"/> in the
/// WS8b field-shape task; the demotion engine (WS8b-2) grades this down for hazardous / degraded findings.
/// </summary>
public enum Confidence
{
    High,
    Medium,
    Low,
}

/// <summary>
/// Advisory shapes that make a finding risky to auto-delete (WS8 §4.2, closed set). Attached by the
/// demotion engine (WS8b-2); this task defines the enum but leaves <see cref="Finding.Hazards"/> empty.
/// </summary>
public enum Hazard
{
    /// <summary>public/protected surface still flagged (classic false-positive shape).</summary>
    PublicApi,

    /// <summary>Symbol/containing type carries a serialization attribute ([JsonProperty], [DataMember], …).</summary>
    SerializationShaped,

    /// <summary>Options/settings-binding shape (*Options, [BindProperties], IConfiguration-bound record).</summary>
    ConfigBoundType,

    /// <summary>A DI/scanning plugin touched this type without producing a keep-alive edge (near-miss).</summary>
    DiPluginShaped,

    /// <summary>The declaring project carries [InternalsVisibleTo] a non-solution assembly.</summary>
    InternalsVisibleTo,

    /// <summary>
    /// (WS3) A &lt;PackageReference&gt; that delivers NO referenceable compile-time assembly — an
    /// analyzer / source-generator / build-only (typically <c>PrivateAssets="all"</c>) package. Its
    /// effect (codegen, MSBuild targets, roslyn analysis) is invisible to symbol edges, so "unused" is
    /// unreliable: EMITTED (never dropped) but demoted to low confidence.
    /// </summary>
    BuildOnlyPackage,
}

/// <summary>
/// The machine action an agent takes to resolve a finding (WS8 §2, closed set). Mapped from
/// <see cref="FindingKind"/> today; the reserved verbs land as their owning work streams do.
/// </summary>
public enum Remediation
{
    /// <summary>Delete the symbol declaration (type/method/property/field/event).</summary>
    DeleteSymbol,

    /// <summary>Remove the &lt;ProjectReference&gt; element from the .csproj.</summary>
    RemoveProjectReference,

    /// <summary>(reserved, WS-enum) remove an unused interface member — a multi-file edit.</summary>
    RemoveFromInterface,

    /// <summary>(WS3) remove the &lt;PackageReference&gt; element from the .csproj.</summary>
    RemovePackageReference,

    /// <summary>(reserved, WS7) delete the production symbol plus its test referrers.</summary>
    DeleteCodeAndTests,
}

/// <summary>A 1-based line/column position in a source file.</summary>
public readonly record struct SourcePosition(int Line, int Column);

/// <summary>
/// (WS7) A test symbol that references a production symbol reported as
/// <see cref="FindingKind.OnlyUsedByTests"/> — the "and its tests" half of the deletion unit (K3).
/// Prose display name + <c>file:line</c>, never a graph key (invariant #1).
/// </summary>
public sealed record TestReferrer(string Symbol, string File, int Line);

/// <summary>
/// The DELETION UNIT for a finding (WS8 §3.3): the full span an agent removes to eliminate the finding,
/// covering leading XML-doc/attribute trivia through the closing brace / terminating semicolon. 1-based.
/// For project/package references it is the single &lt;ProjectReference/&gt; element in the project file.
/// </summary>
public sealed record SourceSpan(string File, SourcePosition Start, SourcePosition End);

/// <summary>
/// A single piece of unreferenced code, with enough location info to jump to it (<see cref="Line"/>/
/// <see cref="Column"/>) AND to delete it (<see cref="Span"/>).
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
    string? ReferencedProject = null) // set only for UnusedProjectReference
{
    /// <summary>
    /// Stable content hash "k1_" + first 10 hex chars of SHA-256(kind ␟ symbol ␟ project [␟ referencedProject])
    /// (WS8 §3.2). Reproducible from published fields, independent of file/line, opaque (never a graph key).
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>The deletion unit (WS8 §3.3). Null only when a declaring syntax node can't be located.</summary>
    public SourceSpan? Span { get; init; }

    /// <summary>How safe autonomous deletion is. Always <see cref="Model.Confidence.High"/> in WS8b-1.</summary>
    public Confidence Confidence { get; init; } = Confidence.High;

    /// <summary>Advisory hazard shapes. Always empty in WS8b-1 (demotion engine is WS8b-2).</summary>
    public IReadOnlyList<Hazard> Hazards { get; init; } = [];

    /// <summary>The machine action an agent takes, mapped from <see cref="Kind"/>.</summary>
    public Remediation Remediation { get; init; } = Remediation.DeleteSymbol;

    /// <summary>
    /// The <see cref="Id"/> of the nearest DEAD symbol keeping this one dead (WS8 §L10). Null when the
    /// finding is directly unreferenced (no incoming edges, or all incoming edges are from live code).
    /// </summary>
    public string? RootCause { get; init; }

    /// <summary>
    /// (WS7) For <see cref="FindingKind.OnlyUsedByTests"/>: the test symbols that reference this
    /// production symbol — the "delete the tests too" half of the remediation unit (K3). Empty for every
    /// other kind. Serialized under <c>details.testReferrers</c> in the JSON output.
    /// </summary>
    public IReadOnlyList<TestReferrer> TestReferrers { get; init; } = [];
}
