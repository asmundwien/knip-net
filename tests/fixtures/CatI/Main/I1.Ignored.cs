namespace CatI.I1;

// I1 (IGNORED FILE): this whole file is matched by ignore.files "**/I1.Ignored.cs".
// Its declarations are NEITHER reported NOR walked (the tree is skipped before the walker runs),
// so nothing here reaches the graph. The paired sibling in I1.Kept.cs proves the fixture actually
// reports dead code (anti-vacuous-green); the RED-FLIP in the test walks this file and expects
// NeverWalked() to surface, so a rooted entry point keeps this type alive (member-dead, like I1.Kept).
public sealed class IgnoredDead
{
    // Root (ConfigureServices) — only takes effect when this file is NOT ignored (RED-FLIP).
    public void ConfigureServices() => Used();

    public void Used() { }

    // Dead method. When this file is ignored it never reaches the graph; when walked (RED-FLIP)
    // its containing type is alive so THIS method is the reported symbol.
    public void NeverWalked() { }
}
