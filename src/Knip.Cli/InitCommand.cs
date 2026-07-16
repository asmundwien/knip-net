namespace Knip.Cli;

/// <summary>
/// <c>init --agent</c>: bootstrap the current repository for agent consumption. Writes the canonical
/// agent protocol to <c>.knip/AGENTS.md</c> and creates <c>knip.json</c> when missing. Never runs
/// analysis; never mutates a directory the user did not run from (the target is always the passed-in
/// working directory, not an inferred parent repo).
/// </summary>
internal static class InitCommand
{
    private const string GeneratedConfig =
        """
        {
          "$schema": "https://raw.githubusercontent.com/asmundwien/knip-net/main/schemas/knip.config.schema.json",
          "output": {
            "format": "json"
          }
        }

        """;

    public static int Run(CliOptions options, string workingDirectory, TextWriter output, TextWriter error)
    {
        if (!options.InitAgent)
        {
            error.WriteLine("error: `init` requires --agent.");
            CliOptions.PrintUsage(error);
            return 2;
        }

        var configPath = Path.Combine(workingDirectory, "knip.json");
        var knipDir = Path.Combine(workingDirectory, ".knip");
        var agentsPath = Path.Combine(knipDir, "AGENTS.md");
        var instructions = AgentInstructionsProvider.Text;

        // .knip/AGENTS.md — write the canonical protocol. Idempotent when unchanged; protected from a
        // silent overwrite of hand-edited content unless --force is supplied.
        if (File.Exists(agentsPath))
        {
            var existing = File.ReadAllText(agentsPath).Replace("\r\n", "\n");
            if (existing != instructions && !options.Force)
            {
                error.WriteLine(
                    $"error: {agentsPath} exists with different content. Re-run with --force to overwrite.");
                return 2;
            }
        }

        Directory.CreateDirectory(knipDir);
        File.WriteAllText(agentsPath, instructions);

        // knip.json — create only when missing. --force does NOT overwrite an existing config (that
        // would need a separate --force-config); an existing file is kept byte-for-byte.
        var configCreated = !File.Exists(configPath);
        if (configCreated)
            File.WriteAllText(configPath, GeneratedConfig);

        output.WriteLine(
            "Initialized Knip.NET agent bootstrap:");
        output.WriteLine("  - knip.json");
        output.WriteLine("  - .knip/AGENTS.md");
        output.WriteLine();
        output.WriteLine(
            "Agents should run `dotnet-knip --agent-instructions` or read `.knip/AGENTS.md` before deleting code.");

        if (!configCreated)
            error.WriteLine("note: knip.json already exists; kept unchanged.");

        return 0;
    }
}
