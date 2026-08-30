namespace CatG.G14;

// G14: assigning a property keeps only the explicit setter closure alive.
public sealed class Sample
{
    private int _value;

    public int Value
    {
        get => ReadValue();
        set => WriteValue(value);
    }

    // Root: ConfigureServices is a default entry-point symbol name.
    public void ConfigureServices() => Value = 1;

    private int ReadValue() => _value;
    private void WriteValue(int value) => _value = value;
}
