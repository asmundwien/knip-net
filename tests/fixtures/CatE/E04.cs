namespace CatE.E04;

// E4: user-defined == / != invoked via comparison (no IdentifierName at the use site).
// CORRECT behavior: both operators ALIVE (C# requires them declared as a pair).
public readonly struct Id
{
    public Id(int v) => Value = v;
    public int Value { get; }

    // ALIVE (hypothesis): used via == and != below.
    public static bool operator ==(Id a, Id b) => a.Value == b.Value;
    public static bool operator !=(Id a, Id b) => a.Value != b.Value;

    public override bool Equals(object? obj) => obj is Id other && other.Value == Value;
    public override int GetHashCode() => Value;
}

public sealed class Root
{
    public bool ConfigureServices()
    {
        var eq = new Id(1) == new Id(2);
        var ne = new Id(1) != new Id(2);
        return eq && ne;
    }
}
