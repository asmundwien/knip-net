namespace CatE.E02;

// E2: user-defined operator + invoked via a + b (BinaryExpression, no IdentifierName at use site).
// CORRECT behavior: operator+ is ALIVE.
public readonly struct Money
{
    public Money(int v) => Value = v;
    public int Value { get; }

    // ALIVE (hypothesis): used via a + b below.
    public static Money operator +(Money a, Money b) => new(a.Value + b.Value);

    // DEAD SIBLING: unused operator- -> must be flagged.
    public static Money operator -(Money a, Money b) => new(a.Value - b.Value);
}

public sealed class Root
{
    public int ConfigureServices()
    {
        var sum = new Money(1) + new Money(2);
        return sum.Value;
    }
}
