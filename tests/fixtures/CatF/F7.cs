namespace CatF.F7;

// F7: a member whose name is in EntryPoints.SymbolNames is a root. The test passes a config with the
// NON-default name "CustomEntryPoint" so a green row proves the configured list (not a builtin) is what
// roots it. Rooting the method also roots its containing type (ContainingType walk).
public sealed class Startup
{
    // ALIVE (root): named "CustomEntryPoint", which the test puts in SymbolNames.
    public void CustomEntryPoint() { }

    // DEAD SIBLING: not a configured symbol name, uncalled -> flagged.
    public void Other() { }
}
