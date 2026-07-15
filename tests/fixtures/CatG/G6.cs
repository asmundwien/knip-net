namespace CatG.G6;

// G6: CONTRACT row (enum members) — WS-enum promoted member-level enum dead-code detection.
// Enum members are first-class graph nodes: a member referenced anywhere stays alive; an unused sibling
// in an otherwise-live enum is flagged; a member of a WHOLE-dead enum is NOT reported (the enum TYPE is,
// outermost-only §3.7).

// Alive enum: referenced below, so the TYPE stays alive and its individual members are analyzed.
// Red is used (Color.Red). Green is never referenced -> flagged. Anti-vacuous: the used member is the
// live half, the unused sibling is the dead half of the SAME live enum.
public enum Color
{
    Red,
    Green, // never referenced -> UnusedEnumMember
}

// [Flags] enum with explicit values. Read is OR'd into a live composite (its identifier appears in live
// code) -> alive. Write is OR'd in too -> alive. Execute is never mentioned -> flagged. This pins the
// bitwise-use judgment call: a member combined via `|` in live code IS used.
[System.Flags]
public enum Access
{
    Read = 1,
    Write = 2,
    Execute = 4, // never referenced -> UnusedEnumMember
}

// Whole-dead enum: NEVER referenced. Outermost-only (§3.7) reports the enum TYPE, not each member.
public enum Unused
{
    A,
    B,
}

public sealed class Root
{
    // Root: `ConfigureServices` is a default entry-point symbol name (kept alive without public-API
    // config). It references Color.Red (keeps Color + Red alive) and the Access.Read | Write composite
    // (keeps Access + Read + Write alive), so every "used" member sits under a LIVE source.
    public Color ConfigureServices()
    {
        Access access = Access.Read | Access.Write;
        System.Console.WriteLine(access);
        return Color.Red;
    }
}
