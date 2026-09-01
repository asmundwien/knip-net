namespace CatG.G12;

// G12: a local function owns the references in its body. An uncalled local function must not keep
// member helpers alive; a called sibling proves that reachable local-function bodies still contribute.
public sealed class Sample
{
    // Root: ConfigureServices is a default entry-point symbol name.
    public int ConfigureServices()
    {
        int Used() => UsedHelper();
        int Configure() => UnusedHelper();

        return Used();
    }

    private static int UsedHelper() => 1;

    // DEAD SIBLING: referenced only from the uncalled local function.
    private static int UnusedHelper() => 2;
}
