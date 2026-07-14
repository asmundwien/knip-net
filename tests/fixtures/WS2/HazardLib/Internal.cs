using System.Runtime.CompilerServices;

// Expose internals to Consumer. This is a classic "load-bearing reference that looks unused" hazard:
// the public API surface may be empty, yet Consumer legitimately depends on an INTERNAL type. Because
// Consumer actually references InternalHelper below, a real cross-assembly symbol edge exists and the
// reference is correctly kept (NOT flagged).
[assembly: InternalsVisibleTo("WS2.Consumer")]

namespace WS2.HazardLib;

// Internal, only visible to WS2.Consumer via InternalsVisibleTo. Consumer.UseInternal() touches it.
internal static class InternalHelper
{
    internal static int Secret() => 7;
}
