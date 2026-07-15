namespace CatL.PublicApi
{
    // L13/L14: this fixture has NO in-solution use site for anything, so every symbol below is DEAD and
    // reported. The demotion split is proven by asserting the TIER on each:
    //
    //   DeadPublicApi          — externally visible (public)  -> publicApi hazard
    //                            L13 (publicApiProjects/treatAllPublicAsUsed SET) -> medium
    //                            L14 (neither key set)                            -> low
    //   DeadInternalPlain      — internal, no IVT hazard, no config -> NO hazard  -> stays HIGH
    //
    // DeadInternalPlain is the anti-vacuous sibling: if the engine demoted indiscriminately it would not
    // stay high. It pins that ONLY the publicApi-hazard finding is graded by C2.

    /// <summary>DEAD + publicApi hazard: externally visible, unreferenced across the whole solution.</summary>
    public sealed class DeadPublicApi
    {
        public void Unused() { }
    }

    // DEAD, no hazard: internal, no InternalsVisibleTo, so C2/IVT do not apply -> confidence stays high.
    internal sealed class DeadInternalPlain
    {
        internal void Unused() { }
    }
}
