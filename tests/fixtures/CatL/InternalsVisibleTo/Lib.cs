using System.Runtime.CompilerServices;

// L17: friend access to an assembly that is NOT part of the analyzed solution. An invisible external
// consumer may bind this project's friend-visible surface, so the tool cannot see whether it is truly
// dead. Those findings are demoted to low via internalsVisibleTo (the same "unknown consumer" logic as
// unconfigured publicApi). "CatL.NotInSolution" is deliberately never a project in this fixture.
[assembly: InternalsVisibleTo("CatL.NotInSolution")]

namespace CatL.InternalsVisibleTo
{
    // Rooted so the private dead sibling below is REPORTED (a private top-level type is illegal; the
    // anti-vacuous private finding must live inside a live type).
    public sealed class Program
    {
        public static void Main() => _ = new InternalContainer.PublicNested();

        // DEAD control: an ordinary external consumer can bind this, so publicApi keeps precedence.
        public void PublicApiControl() { }

        // DEAD, no hazard: a PRIVATE member is invisible even to a friend assembly, so neither the
        // publicApi nor internalsVisibleTo hazard applies -> confidence stays HIGH. This anti-vacuous
        // sibling proves IVT does not tag everything in the project.
        private void DeadPrivate() { }
    }

    // LIVE internal path: a friend assembly can name the public nested type and member even though an
    // ordinary external consumer cannot cross the internal containing type.
    internal static class InternalContainer
    {
        public sealed class PublicNested
        {
            public void PublicButFriendVisible() { }

            // DEAD control: the private barrier remains inaccessible to a friend assembly.
            private void PrivateButContained() { }
        }
    }

    // DEAD + internalsVisibleTo hazard: internal, unreferenced, in a project with IVT to a non-solution
    // assembly -> low. This is the row under test.
    internal sealed class DeadInternal
    {
        internal void Unused() { }
    }
}
