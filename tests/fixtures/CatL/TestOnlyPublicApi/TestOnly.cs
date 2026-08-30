using System;

namespace CatL.TestOnlyPublicApi
{
    // L18 — the collision row (HUMAN DECISION 2026-07-15, §6): C2 (publicApi) precedes C4
    // (deleteCodeAndTests). Two production symbols, each reachable ONLY via a [Fact] test root, so in
    // production mode BOTH are OnlyUsedByTests (remediation DeleteCodeAndTests). They differ ONLY in
    // accessibility, which is exactly what the reordered rule keys on:
    //
    //   PublicTestOnly   (PUBLIC)   -> carries the publicApi hazard -> graded by C2:
    //                                    unconfigured (no publicApiProjects/treatAllPublicAsUsed) -> LOW
    //                                    configured-but-not-listed (posture declared)             -> MEDIUM
    //   InternalTestOnly (INTERNAL) -> no publicApi hazard -> falls through to C4                 -> MEDIUM
    //
    // A genuine production root (Entry.Main -> KeepAlive) keeps the Service TYPE alive so both findings
    // land at MEMBER granularity rather than collapsing to the whole type under outermost-only.

    // Local test-framework attribute (zero NuGet), configured as an explicit fixture alias.
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FactAttribute : Attribute { }

    public sealed class Service
    {
        // PRODUCTION mode: OnlyUsedByTests. PUBLIC -> publicApi hazard -> C2 (unconfigured=low/configured=medium).
        public void PublicTestOnly() { }

        // PRODUCTION mode: OnlyUsedByTests. INTERNAL -> no publicApi hazard -> C4 -> medium.
        internal void InternalTestOnly() { }

        // Keeps the Service TYPE alive in every mode so the two findings report at member granularity.
        public void KeepAlive() { }
    }

    public sealed class Entry
    {
        // A real production root (Main is a default entry-point symbol name) exercising Service.
        public static void Main()
        {
            new Service().KeepAlive();
        }
    }

    public sealed class ServiceTests
    {
        [Fact]
        public void Exercises_PublicTestOnly()
        {
            new Service().PublicTestOnly();
        }

        [Fact]
        public void Exercises_InternalTestOnly()
        {
            new Service().InternalTestOnly();
        }
    }
}
