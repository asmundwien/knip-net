namespace CatE.E15;

// Boolean contexts and short-circuiting operators invoke true/false and &/| without identifiers.
public readonly struct Flag
{
    public Flag(bool value) => Value = value;

    private bool Value { get; }

    // ALIVE: selected by if, conditional-and, and conditional-or expressions below.
    public static bool operator true(Flag value) => value.Value;
    public static bool operator false(Flag value) => !value.Value;
    public static Flag operator &(Flag left, Flag right) => new(left.Value & right.Value);
    public static Flag operator |(Flag left, Flag right) => new(left.Value | right.Value);

    // DEAD SIBLING: another binary operator on the same live type.
    public static Flag operator +(Flag left, Flag right) => new(left.Value | right.Value);
}

public sealed class Root
{
    public int ConfigureServices()
    {
        var left = new Flag(true);
        var right = new Flag(false);
        var score = 0;
        if (left)
            score++;
        if (left && right)
            score++;
        if (left || right)
            score++;
        return score;
    }
}
