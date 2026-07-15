using System.Reflection;

namespace Knip.Cli;

/// <summary>
/// The single source of truth for the agent-consumer protocol. Both <c>--agent-instructions</c>
/// (stdout) and <c>init --agent</c> (writes <c>.knip/AGENTS.md</c>) emit exactly this text, read
/// once from the embedded <c>Resources/AgentInstructions.md</c>. Do NOT duplicate the protocol as a
/// second string, template, or copied Markdown block — an installed global tool must not depend on a
/// source-checkout path for it.
/// </summary>
internal static class AgentInstructionsProvider
{
    private const string ResourceName = "Knip.Cli.Resources.AgentInstructions.md";

    private static readonly Lazy<string> Lazy = new(Read);

    /// <summary>The canonical agent-consumer protocol, normalized to <c>\n</c> line endings.</summary>
    public static string Text => Lazy.Value;

    private static string Read()
    {
        var assembly = typeof(AgentInstructionsProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"embedded resource '{ResourceName}' not found; available: "
                + string.Join(", ", assembly.GetManifestResourceNames()));
        using var reader = new StreamReader(stream);
        // Normalize to \n so byte-for-byte comparisons (stdout vs written file vs provider) hold
        // regardless of the checkout's line-ending policy.
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}
