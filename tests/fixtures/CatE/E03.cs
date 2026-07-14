namespace CatE.E03;

// E3: user-defined implicit conversion invoked via assignment/argument (no IdentifierName at use).
// CORRECT behavior: the implicit conversion operator is ALIVE.
public readonly struct Celsius
{
    public Celsius(double v) => Value = v;
    public double Value { get; }

    // ALIVE (hypothesis): triggered by the implicit conversion below.
    public static implicit operator Celsius(double v) => new(v);

    // DEAD SIBLING: an unused explicit conversion -> must be flagged.
    public static explicit operator double(Celsius c) => c.Value;
}

public sealed class Root
{
    public double ConfigureServices()
    {
        Celsius c = 21.5; // implicit conversion use-site
        return c.Value;
    }
}
