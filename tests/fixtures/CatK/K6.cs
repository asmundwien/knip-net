using CatK;

namespace CatK.K6;

// K6 (G-feat, PRODUCTION mode): production code used by BOTH production and tests is NEVER flagged.
// Tests must not TAINT liveness: a genuine production caller keeps the method alive even in production
// mode, so it is not OnlyUsedByTests. Compiled today; Skip-tagged (WS7). The finding set for K6 must
// be EMPTY under production mode.
public sealed class Shared
{
    // Used by production (ProductionEntry) AND by a test -> alive in every mode, never OnlyUsedByTests.
    public void UsedByBoth() { }
}

public sealed class ProductionEntry
{
    // A real production root (Main is a default entry-point symbol name) that exercises Shared, so
    // Shared.UsedByBoth has a non-test caller.
    public static void Main()
    {
        new Shared().UsedByBoth();
    }
}

public sealed class SharedTests
{
    [Fact]
    public void Also_Exercises_UsedByBoth()
    {
        new Shared().UsedByBoth();
    }
}
