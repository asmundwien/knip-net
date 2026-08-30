using System;

namespace CatG.G16;

// G16: custom event accessors form one C# deletion unit. Either += or -= keeps the whole event and both
// accessor closures alive; an entirely unused sibling is reported once at the event boundary.
public sealed class Publisher
{
    public event Action? AddOnly
    {
        add => AddOnlyAdd(value);
        remove => AddOnlyRemove(value);
    }

    public event Action? RemoveOnly
    {
        add => RemoveOnlyAdd(value);
        remove => RemoveOnlyRemove(value);
    }

    public event Action? Unused
    {
        add => UnusedAdd(value);
        remove => UnusedRemove(value);
    }

    private static void AddOnlyAdd(Action? value) { }
    private static void AddOnlyRemove(Action? value) { }
    private static void RemoveOnlyAdd(Action? value) { }
    private static void RemoveOnlyRemove(Action? value) { }
    private static void UnusedAdd(Action? value) { }
    private static void UnusedRemove(Action? value) { }
}

public sealed class Root
{
    // Root: ConfigureServices is a default entry-point symbol name.
    public void ConfigureServices()
    {
        var publisher = new Publisher();
        publisher.AddOnly += Handler;
        publisher.RemoveOnly -= Handler;
    }

    private static void Handler() { }
}
