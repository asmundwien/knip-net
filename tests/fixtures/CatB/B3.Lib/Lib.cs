using System.Runtime.CompilerServices;

// B3: friend-assembly access. The consumer can bind INTERNAL members of this lib because of
// [InternalsVisibleTo]. The cross-project edge to an internal member must still resolve by
// doc-comment ID (accessibility does not change the graph key).
[assembly: InternalsVisibleTo("CatB.B3.Consumer")]

namespace CatB.B3;

public sealed class Engine
{
    // B3 ALIVE: internal, used only from the friend consumer project (cross-project). Must be alive.
    internal void InternalUsedByFriend() { }

    // B3 DEAD SIBLING: identical internal shape, no caller in any project -> flagged.
    internal void InternalUnused() { }
}
