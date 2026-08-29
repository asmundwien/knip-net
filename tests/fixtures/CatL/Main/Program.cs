using System;

namespace CatL.Main
{
    // The rooted entry point keeps Program + Used() alive. Everything else in this fixture is a
    // deliberate dead island exercised by the WS8b enrichment (id/span/rootCause) battery rows.
    public sealed class Program
    {
        public static void Main() => new Program().Used();

        // ALIVE: reaches sibling declarators in a shared field/event declaration.
        public void Used() => new SharedDeclarations().UseSiblings();
    }

    /// <summary>
    /// DEAD (L4): a dead type whose deletion unit spans this XML-doc comment, the attribute list, and
    /// the whole body through the closing brace. Deleting exactly the reported span must leave the file
    /// compiling.
    /// </summary>
    [Obsolete("dead")]
    public sealed class DeadDocumented
    {
        /// <summary>DEAD member — not reported on its own (its containing type is dead / outermost-only).</summary>
        public void NeverCalled() { }
    }

    // DEAD (L10 root): DeadCaller is directly unreferenced (rootCause == null) but it holds a field
    // typed as DeadCallee, so DeadCallee is kept dead ONLY by DeadCaller (rootCause == DeadCaller's id).
    public sealed class DeadCaller
    {
        private DeadCallee? _callee;
    }

    // DEAD (L10 cascade): the only incoming edge is DeadCaller's field, which is itself dead.
    public sealed class DeadCallee
    {
    }
}
