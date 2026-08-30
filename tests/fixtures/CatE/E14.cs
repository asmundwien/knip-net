namespace CatE.E14;

// Unary and increment expressions bind operator methods without an identifier at the call site.
public readonly struct Number
{
    public Number(int value) => Value = value;

    public int Value { get; }

    // ALIVE: invoked by the rooted method through unary and postfix expressions.
    public static Number operator -(Number value) => new(-value.Value);
    public static Number operator ++(Number value) => new(value.Value + 1);

    // DEAD SIBLINGS: same implicit-invocation shapes, never selected by the compiler.
    public static Number operator ~(Number value) => new(~value.Value);
    public static Number operator --(Number value) => new(value.Value - 1);
}

public sealed class Root
{
    public int ConfigureServices()
    {
        var value = -new Number(1);
        value++;
        return value.Value;
    }
}
