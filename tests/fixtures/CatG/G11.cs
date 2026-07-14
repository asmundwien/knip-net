namespace CatG.G11;

// G11: expression-bodied members behave identically to block-bodied ones. A used expression-bodied
// method/property stays alive; an unused expression-bodied method is flagged.
public sealed class Sample
{
    // Root (expression-bodied): reaches the used expression-bodied members.
    public int ConfigureServices() => UsedMethod() + UsedProperty;

    // ALIVE: expression-bodied method called from the root.
    private int UsedMethod() => 1;

    // ALIVE: expression-bodied property read from the root.
    private int UsedProperty => 2;

    // DEAD SIBLING: expression-bodied method with no caller -> flagged.
    private int UnusedMethod() => 3;
}
