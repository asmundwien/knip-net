using System.Runtime.CompilerServices;

// L17: friend access to an assembly that is NOT part of the analyzed solution. An invisible external
// consumer may bind this project's internals, so the tool cannot see whether they are truly dead —
// INTERNAL findings here are demoted to low via the internalsVisibleTo hazard (same "unknown consumer"
// logic as unconfigured publicApi). "CatL.NotInSolution" is deliberately never a project in this fixture.
[assembly: InternalsVisibleTo("CatL.NotInSolution")]

namespace CatL.InternalsVisibleTo
{
    // Rooted so the private dead sibling below is REPORTED (a private top-level type is illegal; the
    // anti-vacuous private finding must live inside a live type).
    public sealed class Program
    {
        public static void Main() { }

        // DEAD, no hazard: a PRIVATE member is invisible even to a friend assembly, so neither the
        // publicApi nor the internalsVisibleTo hazard applies -> confidence stays HIGH. This is the
        // anti-vacuous sibling proving IVT tags ONLY internal findings, not everything in the project.
        private void DeadPrivate() { }
    }

    // DEAD + internalsVisibleTo hazard: internal, unreferenced, in a project with IVT to a non-solution
    // assembly -> low. This is the row under test.
    internal sealed class DeadInternal
    {
        internal void Unused() { }
    }
}
