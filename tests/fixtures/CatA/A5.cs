namespace CatA.A5;

// A5: a self-recursive method with no external caller is still dead -> flagged.
// (Its only inbound edge is its own self-reference, which confers no reachability from a root.)
public sealed class Sample
{
    public void ConfigureServices() => Live();

    // ALIVE control: called from root, also recursive.
    private void Live(int n)
    {
        if (n > 0) Live(n - 1);
    }

    private void Live() => Live(1);

    // DEAD: recursive but unreachable from any root.
    private void Recurse(int n)
    {
        if (n > 0) Recurse(n - 1);
    }
}
