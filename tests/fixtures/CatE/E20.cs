namespace CatE.E20;

public readonly struct Source
{
    public Source(int value) => Value = value;
    public int Value { get; }
}

public readonly struct Target
{
    public Target(int value) => Value = value;
    public int Value { get; }

    // ALIVE: foreach converts each Current value to the iteration-variable type.
    public static implicit operator Target(Source value) => new(value.Value);

    // DEAD SIBLING: the reverse conversion is never selected.
    public static explicit operator Source(Target value) => new(value.Value);
}

public sealed class Values
{
    public Enumerator GetEnumerator() => new();

    public struct Enumerator
    {
        private bool _moved;
        public bool MoveNext() => !_moved && (_moved = true);
        public Source Current => new(7);
    }
}

public sealed class Root
{
    public int ConfigureServices()
    {
        foreach (Target value in new Values())
            return value.Value;
        return 0;
    }
}
