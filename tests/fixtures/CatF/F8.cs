namespace CatF.F8;

// F8: proves the entry-point config actually applies. This type is rooted by DEFAULT config (its name
// matches the default "*Controller" NamePattern). The F8 test runs with a config whose EntryPoints has
// ALL-EMPTY lists, so no default rooting survives: the type is entirely unreferenced and is flagged
// (outermost). A green row means "config replacement removed the framework default that had kept it
// alive" — the RED-FLIP evidence for the whole Category F rooting story.
public sealed class EmptyProbeController
{
    public void Index() { }
}
