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

        var docId = definition.GetDocumentationCommentId();
        if (docId is null)
            return null;

        // Qualify the key with the DEFINING assembly so two symbols that share a namespace + type +
        // signature in DIFFERENT assemblies get DISTINCT graph nodes (fix B6). Doc-comment IDs carry no
        // assembly, so without this the two copies collapse into one node and one project's use keeps
        // the other project's dead copy alive (a false negative).
        //
        // Invariant #1 is preserved: we qualify with definition.ContainingAssembly — the assembly that
        // DEFINES the symbol — which is identical whether the symbol is seen from its own source or from
        // a referencing project (source vs. metadata). The key stays a plain string derived from the
        // doc-comment ID, so a symbol defined in project A still maps to the same key when used from
        // project B. A null ContainingAssembly (e.g. dynamic/error symbols) falls back to the bare docId.
        var assemblyName = definition.ContainingAssembly?.Name;
        return assemblyName is null ? docId : assemblyName + "::" + docId;
    }
}
