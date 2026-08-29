using System.Security.Cryptography;
using System.Text;
using Knip.Core.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Knip.Core.Analysis;

/// <summary>
/// Computes the published enrichment fields on a <see cref="Finding"/> (WS8 §3): the stable content-hash
/// <c>id</c>, the deletion-unit <c>span</c>, and the <c>remediation</c> mapped from the finding kind.
/// Confidence/hazard demotion is a separate task (WS8b-2); here confidence stays High and hazards empty.
/// </summary>
internal static class FindingEnrichment
{
    /// <summary>U+001F unit separator — cannot appear in any of the hashed display fields.</summary>
    private const char Separator = '';

    /// <summary>
    /// Stable finding id (WS8 §3.2): "k1_" + first 10 lower-hex chars of SHA-256 over
    /// kind ␟ symbol ␟ project [␟ referencedProject], UTF-8, no BOM. Content hash, NOT a graph key
    /// (invariant #1); reproducible from published fields and independent of file/line.
    /// </summary>
    public static string ComputeId(FindingKind kind, string symbol, string project, string? referencedProject)
    {
        var builder = new StringBuilder();
        builder.Append(CamelCase(kind));
        builder.Append(Separator);
        builder.Append(symbol);
        builder.Append(Separator);
        builder.Append(project);
        if (referencedProject is not null)
        {
            builder.Append(Separator);
            builder.Append(referencedProject);
        }

        byte[] hash;
        using (var sha = SHA256.Create())
            hash = sha.ComputeHash(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString()));

        var hex = new StringBuilder("k1_", 13);
        for (var i = 0; hex.Length < 13; i++)
            hex.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return hex.ToString(0, 13); // "k1_" + 10 hex chars
    }

    /// <summary>The serialized (camelCase) form of a kind — must match what the JSON reporter emits.</summary>
    public static string CamelCase(FindingKind kind)
    {
        var name = kind.ToString();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    public static Remediation RemediationFor(FindingKind kind) => kind switch
    {
        FindingKind.UnusedProjectReference => Remediation.RemoveProjectReference,
        FindingKind.UnusedPackageReference => Remediation.RemovePackageReference,
        FindingKind.OnlyUsedByTests => Remediation.DeleteCodeAndTests,
        _ => Remediation.DeleteSymbol, // type/method/property/field/event
    };

    /// <summary>
    /// The ADVISORY hazards (WS8 §4.2) for a symbol finding — never changes the emitted set, only tags
    /// risk (the demotion of confidence off these tags is <see cref="ConfidenceModel"/>). Detected here:
    /// <list type="bullet">
    ///   <item><see cref="Hazard.PublicApi"/> — the symbol and its complete containing-type chain are
    ///     externally visible: a consumer outside the solution may bind it.</item>
    ///   <item><see cref="Hazard.InternalsVisibleTo"/> — the symbol and its complete containing-type
    ///     chain are visible to a friend assembly, but not to an ordinary external consumer, and its
    ///     declaring project carries an <c>[InternalsVisibleTo]</c> naming an assembly NOT in the solution
    ///     (an invisible friend consumer), signalled by
    ///     <paramref name="declaringProjectHasIvtToNonSolution"/>.</item>
    /// </list>
    /// Runtime-only hazards are computed separately by <see cref="ComputeRuntimeHazards"/>.
    /// </summary>
    public static IReadOnlyList<Hazard> ComputeHazards(ISymbol symbol, bool declaringProjectHasIvtToNonSolution)
    {
        var hazards = new List<Hazard>();

        if (SymbolVisibility.IsExternallyVisible(symbol))
            hazards.Add(Hazard.PublicApi);
        else if (declaringProjectHasIvtToNonSolution && SymbolVisibility.IsVisibleToFriendAssembly(symbol))
            hazards.Add(Hazard.InternalsVisibleTo);

        return hazards.Count == 0 ? [] : hazards;
    }


    // Member attribute simple names (with or without the "Attribute" suffix) that mark a member serialized.
    private static readonly HashSet<string> MemberSerializationAttributes = new(StringComparer.Ordinal)
    {
        "JsonPropertyName", // System.Text.Json.Serialization.JsonPropertyNameAttribute
        "JsonProperty",     // Newtonsoft.Json.JsonPropertyAttribute
        "DataMember",       // System.Runtime.Serialization.DataMemberAttribute
    };

    // Type-level attribute simple names that mark a whole type serialized (all its data members are shaped).
    private static readonly HashSet<string> TypeSerializationAttributes = new(StringComparer.Ordinal)
    {
        "Serializable",  // System.SerializableAttribute
        "DataContract",  // System.Runtime.Serialization.DataContractAttribute
    };

    /// <summary>
    /// Runtime-only advisory hazards for deletions that can compile and pass tests but fail at runtime.
    /// Never changes the emitted set; <see cref="ConfidenceModel"/> demotes these findings to low.
    /// Detection is conservative: false hazard positives reduce autonomy, while false negatives can make
    /// an unsafe deletion appear eligible.
    /// <list type="bullet">
    ///   <item><see cref="Hazard.SerializationShaped"/> — a DATA MEMBER (non-indexer property / non-const
    ///     field) that either wears a member serialization attribute, sits in a type wearing a type-level
    ///     serialization attribute, or belongs to a recognized serializer target or one of its collection
    ///     element types (<paramref name="serializationUsageTypes"/>). Methods are never tagged.</item>
    ///   <item><see cref="Hazard.ConfigBoundType"/> — a PUBLIC PROPERTY of a type bound from configuration
    ///     (<paramref name="configBoundTypes"/>).</item>
    ///   <item><see cref="Hazard.DiPluginShaped"/> — a symbol in the constructor dependency closure of a
    ///     recognized DI registration whose factory or instance overload does not prove activation.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<Hazard> ComputeRuntimeHazards(
        ISymbol symbol,
        ISet<string> serializationUsageTypes,
        ISet<string> configBoundTypes,
        ISet<string> diPluginShapedSymbols)
    {
        var hazards = new List<Hazard>();
        var symbolId = SymbolId.For(symbol);
        var containingTypeId = symbol.ContainingType is { } ct ? SymbolId.For(ct) : null;

        var isDataMember = symbol is IPropertySymbol { IsIndexer: false } or IFieldSymbol { IsConst: false };
        if (isDataMember)
        {
            var serializationShaped =
                WearsAnySerializationAttribute(symbol, MemberSerializationAttributes)
                || (symbol.ContainingType is { } type && WearsAnySerializationAttribute(type, TypeSerializationAttributes))
                || (containingTypeId is not null && serializationUsageTypes.Contains(containingTypeId));
            if (serializationShaped) hazards.Add(Hazard.SerializationShaped);

            // Config binding populates public PROPERTIES of the bound type.
            if (symbol is IPropertySymbol { IsIndexer: false, DeclaredAccessibility: Accessibility.Public }
                && containingTypeId is not null && configBoundTypes.Contains(containingTypeId))
                hazards.Add(Hazard.ConfigBoundType);
        }

        if (symbolId is not null && diPluginShapedSymbols.Contains(symbolId))
            hazards.Add(Hazard.DiPluginShaped);

        return hazards.Count == 0 ? [] : hazards;
    }

    private static bool WearsAnySerializationAttribute(ISymbol symbol, HashSet<string> names)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;
            if (name is null) continue;
            var trimmed = name.EndsWith("Attribute", StringComparison.Ordinal)
                ? name[..^"Attribute".Length]
                : name;
            if (names.Contains(trimmed)) return true;
        }
        return false;
    }

    /// <summary>
    /// True when the compilation's assembly carries an <c>[InternalsVisibleTo("X")]</c> whose target
    /// assembly <c>X</c> is NOT one of <paramref name="solutionAssemblies"/> — i.e. an INVISIBLE external
    /// friend that may bind this project's internals (same "unknown consumer" logic as unconfigured
    /// publicApi). The target name is the bare assembly name; strip any <c>, PublicKey=…</c> suffix.
    /// Read from assembly attributes (not source) so metadata-only viewpoints agree.
    /// </summary>
    public static bool HasInternalsVisibleToNonSolutionAssembly(
        Compilation compilation, ISet<string> solutionAssemblies)
    {
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "InternalsVisibleToAttribute") continue;
            if (attribute.ConstructorArguments.Length == 0) continue;
            if (attribute.ConstructorArguments[0].Value is not string target) continue;

            // "Friend, PublicKey=00240000..." → "Friend". IVT targets are bare assembly names.
            var comma = target.IndexOf(',');
            var name = (comma >= 0 ? target.Substring(0, comma) : target).Trim();
            if (name.Length > 0 && !solutionAssemblies.Contains(name))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The single-file deletion unit (WS8 §3.3) for a symbol: from the earliest leading XML-doc /
    /// attribute trivia through the declaration's closing brace / terminating semicolon. Returns null
    /// when the symbol has no complete, independently removable source declaration: none is available,
    /// it spans multiple declarations, or it shares a field/event declaration with another declarator.
    /// </summary>
    public static SourceSpan? ComputeSpan(ISymbol symbol)
    {
        var references = symbol.DeclaringSyntaxReferences;
        if (references.Length != 1) return null;

        // Roslyn exposes partial method definition and implementation parts as paired method symbols;
        // each part may itself report one syntax reference, but neither is a complete deletion unit.
        if (symbol is IMethodSymbol { PartialDefinitionPart: not null }
            or IMethodSymbol { PartialImplementationPart: not null })
            return null;

        var node = references[0].GetSyntax();

        // One field/event statement owns every declarator. Reporting a span for one symbol would instruct
        // consumers to delete any live siblings, so this finding intentionally has no single-symbol action.
        if (node is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration }
            && declaration.Variables.Count != 1)
            return null;

        // Fields/events declare a VariableDeclarator; its single-declarator owning statement is the unit.
        if (node is VariableDeclaratorSyntax or VariableDeclarationSyntax)
        {
            var owner = node.FirstAncestorOrSelf<BaseFieldDeclarationSyntax>();
            if (owner is not null) node = owner;
        }

        var tree = node.SyntaxTree;
        var text = tree.GetText();

        // Full span INCLUDING leading trivia that belongs to this declaration (XML-doc + attributes).
        // node.FullSpan swallows ALL leading trivia; trim back to the start of the doc-comment/attribute
        // run so an unrelated preceding blank line or a comment on the previous member is not consumed.
        var start = StartIncludingOwnedTrivia(node);
        var end = node.Span.End; // node.Span excludes trailing trivia; end at the last token.

        var startLine = text.Lines.GetLinePosition(start);
        var endLine = text.Lines.GetLinePosition(end);

        return new SourceSpan(
            tree.FilePath,
            new SourcePosition(startLine.Line + 1, startLine.Character + 1),
            new SourcePosition(endLine.Line + 1, endLine.Character + 1));
    }

    /// <summary>
    /// The offset at which the deletion unit starts: the first character of the earliest leading
    /// XML-doc comment or attribute-list trivia directly owned by the declaration, else the first token.
    /// Skips whitespace/end-of-line trivia and does not reach past a blank line or a preceding member's
    /// trailing comment.
    /// </summary>
    private static int StartIncludingOwnedTrivia(SyntaxNode node)
    {
        var leading = node.GetLeadingTrivia();
        var firstToken = node.GetFirstToken();

        // Walk from the LAST leading trivia backwards; keep doc-comments and (whitespace/EOL that
        // immediately precedes the declaration's own trivia). Stop at the first blank line or ordinary
        // comment that belongs to the previous member.
        int startOffset = firstToken.SpanStart;
        for (var i = leading.Count - 1; i >= 0; i--)
        {
            var trivia = leading[i];
            switch (trivia.Kind())
            {
                case SyntaxKind.SingleLineDocumentationCommentTrivia:
                case SyntaxKind.MultiLineDocumentationCommentTrivia:
                    // XML-doc belongs to THIS declaration; include it and any whitespace before it.
                    startOffset = trivia.FullSpan.Start;
                    break;
                case SyntaxKind.WhitespaceTrivia:
                case SyntaxKind.EndOfLineTrivia:
                    // Interior whitespace/EOL between owned trivia and the token — keep scanning; it is
                    // only kept if a doc-comment further up claims it (startOffset moves there).
                    continue;
                default:
                    // A regular comment, #region, blank-line boundary, etc.: belongs to prior context.
                    // Stop; do not swallow it.
                    i = -1;
                    break;
            }
        }

        // Attribute lists are CHILD nodes of the declaration (not leading trivia): node.Span already
        // starts at the first attribute's '[' when present, and firstToken is that '['. So the
        // attribute run is naturally inside [firstToken.SpanStart .. node.Span.End]. Only the doc-comment
        // (which IS leading trivia) needs the walk above. Take the earliest of the two.
        return Math.Min(startOffset, node.Span.Start);
    }
}
