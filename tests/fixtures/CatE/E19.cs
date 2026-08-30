using System.Runtime.CompilerServices;

namespace CatE.E19;

[InterpolatedStringHandler]
public struct Handler
{
    public Handler(int literalLength, int formattedCount)
    {
        _ = Initialize(literalLength + formattedCount);
    }

    // ALIVE: reached only through the compiler-bound handler constructor.
    private static int Initialize(int value) => value;

    // DEAD SIBLING: same helper shape, never called by the selected constructor.
    private static int InitializeOther(int value) => value;

    // ALIVE: compiler-generated append calls for literal and formatted parts.
    public void AppendLiteral(string value) { }
    public void AppendFormatted<T>(T value) { }

    // DEAD SIBLING: same append shape, never selected by interpolation lowering.
    public void AppendOther(string value) { }
}

public static class Sink
{
    public static void Write(Handler handler) { }
}

public sealed class Root
{
    public void ConfigureServices()
    {
        Sink.Write($"value: {42}");
    }
}
