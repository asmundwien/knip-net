using Microsoft.CodeAnalysis;

namespace Knip.Core.Analysis;

/// <summary>
/// Stable, cross-compilation identity for a symbol. Roslyn does not guarantee reference equality
/// for the "same" symbol seen from different projects (source vs. metadata), so we key the
/// reachability graph on the documentation comment ID (e.g. "M:Ns.Type.Method(System.String)"),
/// which is identical whether the symbol is resolved from source or from a referenced assembly.
/// </summary>
internal static class SymbolId
{
    public static string? For(ISymbol symbol)
    {
        var definition = symbol.OriginalDefinition;
        if (definition is IMethodSymbol { ReducedFrom: { } reduced })
            definition = reduced.OriginalDefinition;
        return definition.GetDocumentationCommentId();
    }
}
