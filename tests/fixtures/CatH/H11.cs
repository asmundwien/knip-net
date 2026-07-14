using System;

namespace CatH.H11;

// H11 (DECIDED 2026-07-15): WALK built-in generated trees for their outbound edges/roots (keeping the
// user symbols they reference alive) while NEVER reporting declarations INSIDE them. The generated
// counterpart lives in H11.Generated.g.cs (matched by the built-in "*.g.cs" generated pattern), which
// is now WALKED — not dropped — so its edges into user code confer liveness.
//
// This fixture demonstrates ALL THREE decided behaviors:
//   (a) a user method whose ONLY use is from its generated counterpart -> stays ALIVE;
//   (b) a dead symbol DECLARED inside the generated file -> NEVER reported (see H11.Generated.g.cs);
//   (c) a DECOY: an unrelated dead USER symbol in this (normal) file -> STILL flagged.
public sealed partial class Handler
{
    // Rooted host (ConfigureServices is a default entry-point name) — reaches into the generated tree
    // via the partial Wire() hook whose implementation the generator supplies.
    public void ConfigureServices() => Wire();

    // The generated file implements this partial. ConfigureServices() (a root) calls it, so the
    // generated implementation is REACHABLE — and the edge it records to Invoke() is load-bearing.
    partial void Wire();

    // (a) USER method. Its ONLY caller is the generated Wire() implementation in H11.Generated.g.cs.
    // Because the generated tree is now WALKED, that edge is recorded and Invoke() stays ALIVE. Under
    // the old wholesale-drop behavior the generated edge was lost and Invoke() was flagged dead.
    public void Invoke() { }

    // (c) DECOY — an ordinary dead USER method in a NORMAL file, referenced from nowhere. Walking the
    // generated tree must NOT blanket-root user code, so this MUST still be flagged (anti-over-rooting).
    public void NeverReferenced() { }
}
