namespace CatE.E22;

public sealed class Sequence
{
    // ALIVE: list and slice patterns bind these members implicitly.
    public int Length => 3;
    public int this[int index] => index;
    public Sequence Slice(int start, int length) => this;

    // DEAD SIBLINGS: similar members not selected by pattern binding.
    public int Count => 3;
    public int At(int index) => index;
    public Sequence Subsequence(int start, int length) => this;
}

public sealed class Root
{
    public bool ConfigureServices(Sequence sequence) => sequence is [0, .. var rest];
}
