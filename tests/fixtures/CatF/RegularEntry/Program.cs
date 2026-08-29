namespace CatF.F15;

internal static class Program
{
    public static void Main()
    {
        new OrdinaryHost().Touch();
    }
}

public sealed class OrdinaryHost
{
    public void Touch() { }

    public static void Main() { }

    public static void Main(string[] args) => _ = args;
}
