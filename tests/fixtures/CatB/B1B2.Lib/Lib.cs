namespace CatB.B1B2;

// B1/B2: a library whose members are only reachable from ANOTHER project.
// The graph is keyed on documentation-comment IDs (invariant #1), so a method resolved from
// the consumer's METADATA reference must map to the same node as its source declaration in this lib.
public sealed class Widget
{
    // B1 ALIVE: called only from CatB.B1B2.Consumer.ConfigureServices (cross-project). If invariant #1
    // regresses, the cross-project edge misses this node and B1 flips to flagged -> critical bug.
    public void UsedFromConsumer() { }

    // B2 DEAD SIBLING: identical public shape, no caller in ANY project -> flagged. This is B1's
    // anti-vacuous-green mutation: prove the lib itself is analyzed and public!=automatically-alive.
    public void UnusedInLib() { }
}
