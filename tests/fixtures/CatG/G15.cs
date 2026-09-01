namespace CatG.G15;

// G15: object-initializer assignment keeps the init accessor closure alive without rooting the getter.
public sealed class Sample
{
    private int _value;

    public int Value
    {
        get => ReadValue();
        init => WriteValue(value);
    }

    private int ReadValue() => _value;
    private void WriteValue(int value) => _value = value;
}

public sealed class Root
{
    // Root: ConfigureServices is a default entry-point symbol name.
    public Sample ConfigureServices() => new() { Value = 1 };
}
