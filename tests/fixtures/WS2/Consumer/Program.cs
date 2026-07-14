using System;

namespace WS2.Consumer;

// Production entry point (Main is a default entry-point symbol -> root). It exercises WS2.UsedLib
// (edge -> reference kept) and WS2.HazardLib's internal type (edge -> reference kept). It never
// touches WS2.UnusedLib (no edge -> that reference is flagged unused).
public static class Program
{
    public static void Main()
    {
        Console.WriteLine(global::WS2.UsedLib.Greeter.Hello());
        UseInternal();
    }

    // Uses an INTERNAL type of WS2.HazardLib (visible via [InternalsVisibleTo]). This is the hazard
    // scenario: the reference has no PUBLIC usage but a genuine internal dependency, so it must not be
    // flagged. The cross-assembly edge Consumer -> WS2.HazardLib keeps it alive.
    private static int UseInternal() => global::WS2.HazardLib.InternalHelper.Secret();
}
