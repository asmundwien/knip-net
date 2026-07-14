namespace CatI.I4;

// I4 (KEPT project): its dead member is ALWAYS analyzed/reported. It is the anti-vacuous anchor:
// no matter the ignore.projects setting, KeptDead stays flagged, proving the run reports code.
// A rooted entry point keeps the TYPE alive so the reported symbol is the METHOD.
public sealed class KeptSample
{
    public void ConfigureServices() => Used();
    public void Used() { }

    public void KeptDead() { }
}
