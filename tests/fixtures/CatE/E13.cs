namespace CatE.E13;

// E13: an event subscribed with += and raised. The subscription/raise reference the event by name
// (IdentifierName exists), so this is HYPOTHESIZED already-green. CORRECT behavior: the subscribed
// event is ALIVE and an unused event is flagged.
public sealed class Publisher
{
    // ALIVE (hypothesis): subscribed and raised below.
    public event System.Action? Ping;

    // DEAD SIBLING: an event never subscribed or raised -> must be flagged.
    public event System.Action? Unused;

    public void Raise() => Ping?.Invoke();
}

public sealed class Root
{
    public void ConfigureServices()
    {
        var p = new Publisher();
        p.Ping += () => { };
        p.Raise();
    }
}
