// I3: ignore.namespaces. The whole namespace CatI.I3.Ignored is suppressed via the glob
// "CatI.I3.Ignored"; its dead member is not reported. The dead sibling in the sibling namespace
// CatI.I3.Kept (NOT ignored) is flagged, proving the fixture reports (anti-vacuous-green). A rooted
// entry point in each type keeps the TYPE alive so the reported symbol is the METHOD.

namespace CatI.I3.Ignored
{
    public sealed class Widget
    {
        public void ConfigureServices() => Used();
        public void Used() { }

        // Dead, but its containing namespace is ignored -> NOT reported.
        public void IgnoredNamespaceMethod() { }
    }
}

namespace CatI.I3.Kept
{
    public sealed class Widget
    {
        public void ConfigureServices() => Used();
        public void Used() { }

        // DEAD SIBLING in a non-ignored namespace -> flagged.
        public void KeptNamespaceMethod() { }
    }
}
