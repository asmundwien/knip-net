using Newtonsoft.Json;

namespace LegacyLib
{
    /// <summary>Reachable via the public API surface: serializes a payload with the packages.config-
    /// resolved Newtonsoft.Json reference. A public-API root, so a Knip run keeps it alive.</summary>
    public sealed class LegacyGreeter
    {
        public string Greet(string name)
        {
            return JsonConvert.SerializeObject(new { message = "Hello, " + name });
        }
    }

    /// <summary>Deliberately unused, internal, unreferenced anywhere — the dead-code a Windows e2e run
    /// over this legacy fixture is expected to flag.</summary>
    internal sealed class UnusedLegacyType
    {
        public int Compute()
        {
            return 42;
        }
    }
}
