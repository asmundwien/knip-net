namespace CatG.G2;

// G2: a USED positional record's synthesized members (ctor, Equals, Deconstruct, props, ToString,
// GetHashCode) are compiler-generated and must NEVER be reported. An entirely UNUSED record IS
// flagged (outermost type only).
public record UsedRecord(int X, int Y);

// DEAD SIBLING: an unused record -> flagged (as the outermost dead symbol).
public record UnusedRecord(int A, int B);

public sealed class Root
{
    // Root: constructs and consumes UsedRecord, exercising its synthesized members.
    public int ConfigureServices()
    {
        var r = new UsedRecord(1, 2);
        var (x, y) = r;                 // synthesized Deconstruct
        var same = r == new UsedRecord(1, 2); // synthesized Equals/==
        return x + y + (same ? 0 : 1) + r.X + r.Y; // synthesized properties
    }
}
