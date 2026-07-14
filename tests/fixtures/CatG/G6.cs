namespace CatG.G6;

// G6: DECISION row (enum members). Whether the tool reports dead enum members member-by-member is a
// PRODUCT DECISION reserved for the human. The enum type itself is kept alive (referenced below); the
// question is only about its individual members. This fixture has an unused member (Green) alongside a
// used one (Red). The test captures ACTUAL behavior and is skip-tagged as a decision.
public enum Color
{
    Red,
    Green, // never referenced
}

public sealed class Root
{
    // Root: references the enum type and one member, keeping the type alive.
    public Color ConfigureServices() => Color.Red;
}
