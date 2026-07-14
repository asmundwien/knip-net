using System;

namespace CatH.H11;

// H11 (DECISION): the ONLY reference to Handler.Invoke() lives in generated code (H11.Generated.g.cs).
// By default ignore.files contains "**/*.g.cs", so the engine SKIPS that syntax tree entirely
// (DeadCodeAnalyzer walks trees, `continue`-ing on ignored file paths) — the edge FROM the generated
// file is never recorded, so Invoke() is flagged dead.
// Decision reserved for the human: should the walker walk generated trees for their OUTBOUND edges
// (keeping user symbols alive) while never REPORTING declarations inside generated files?
public sealed class Handler
{
    // Rooted host so the outermost-dead rule doesn't hide the member.
    public void ConfigureServices() { }

    // Referenced ONLY from H11.Generated.g.cs (an ignored file) -> flagged dead under default ignore.
    public void Invoke() { }

    // DEAD SIBLING (honest): referenced from nowhere at all -> flagged regardless of the decision.
    public void NeverReferenced() { }
}
