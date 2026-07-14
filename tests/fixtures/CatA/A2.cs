namespace CatA.A2;

// A2: an unused PUBLIC method is flagged when public-as-used is OFF (the default config).
public sealed class Sample
{
    public void ConfigureServices() => UsedPublic();

    // DEAD SIBLING: public but uncalled -> flagged (no TreatAllPublicAsUsed).
    public void UnusedPublic() { }

    // ALIVE: reached from the root.
    public void UsedPublic() { }
}
