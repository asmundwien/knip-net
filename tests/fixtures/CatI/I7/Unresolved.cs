namespace CatI.I7;

// I7: a deliberately UNRESOLVED type. NoSuchType is never declared or imported anywhere, so Roslyn
// binds it to a TypeKind.Error symbol. ReferenceWalker.AddTypeReference increments
// UnresolvedTypeReferences on the error type, and DeadCodeAnalyzer inserts the unresolved-type
// WARNING into AnalysisResult.LoadDiagnostics (invariant #6). Zero NuGet — the "missing" type is
// simply undeclared, so this stays offline/deterministic.
public sealed class Sample
{
    // Parameter type NoSuchType does not exist -> Roslyn error type -> unresolved warning path.
    public void Consume(NoSuchType value) { }
}
