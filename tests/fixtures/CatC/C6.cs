namespace CatC.C6;

// C6: extension method invoked via extension syntax (value.Used()). The invocation binds an
// IdentifierName to the extension method -> ALIVE. A same-shaped extension never invoked is flagged.
public sealed class Widget { }

public static class WidgetExtensions
{
    // ALIVE: invoked as widget.Used() below.
    public static int Used(this Widget widget) => 1;

    // DEAD SIBLING: extension method never invoked via either syntax -> flagged.
    public static int Unused(this Widget widget) => 2;
}

public sealed class Sample
{
    public int ConfigureServices() => new Widget().Used();
}
