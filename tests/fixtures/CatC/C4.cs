namespace CatC.C4;

// C4: method group used as a delegate (Transform passed where a Func<int,int> is expected). The
// method-group identifier binds to Transform, recording a normal reference edge -> Transform ALIVE.
// Select is mimicked locally so the fixture stays offline.
public sealed class Sample
{
    // Local mimic of Enumerable.Select's shape; body irrelevant.
    private static int Apply(int input, System.Func<int, int> projection) => projection(input);

    public int ConfigureServices() => Apply(1, Transform);

    // ALIVE: passed as a method group to Apply.
    public int Transform(int x) => x + 1;

    // DEAD SIBLING: same-shaped method-group candidate, never passed anywhere -> flagged.
    public int Untouched(int x) => x - 1;
}
