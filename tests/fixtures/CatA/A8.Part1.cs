namespace CatA.A8;

// A8 (file 1 of 2): a partial class + partial method split across two files. When the partial method
// is used, the two syntax declarations must unify into ONE graph node (SymbolId is doc-comment-ID,
// so both files map to the same key) and stay alive.
public partial class Sample
{
    // Root: reaches the partial method's implementation (in Part2).
    public void ConfigureServices() => PartialMethod();

    // Partial method DEFINING declaration (signature only).
    public partial void PartialMethod();

    // DEAD SIBLING: a non-partial method of identical shape with no caller -> flagged.
    public void UnusedMethod() { }
}
