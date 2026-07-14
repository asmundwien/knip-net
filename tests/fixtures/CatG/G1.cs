namespace CatG.G1;

// G1: a LOCAL function is a member-internal construct, not a member the tool tracks -> never reported,
// even when unused. DEAD SIBLING: a top-level (member) method with no caller IS flagged.
public sealed class Sample
{
    // Root: ConfigureServices is a default entry-point symbol name.
    public int ConfigureServices()
    {
        int Used() => 1;

        // NOT reported: local function, unused. The tool only tracks type members.
        int Unused() => 2;

        return Used();
    }

    // DEAD SIBLING: a real member method with no caller -> flagged.
    public void UnusedMethod() { }
}
