namespace CatG.G13;

// G13: property accessors are separate reachability and deletion units when their bodies are explicit.
// Reading the property keeps only the getter closure alive; the unused setter remains safely removable.
public sealed class Sample
{
    private int _value;

    public int Value
    {
        get => ReadValue();
        set => WriteValue(value);
    }

    // Root: ConfigureServices is a default entry-point symbol name.
    public int ConfigureServices() => Value;

    private int ReadValue() => _value;
    private void WriteValue(int value) => _value = value;
}
