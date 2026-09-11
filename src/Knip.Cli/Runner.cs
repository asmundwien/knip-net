using Knip.Core;
using Knip.Core.Configuration;
using Knip.Core.Model;

namespace Knip.Cli;

/// <summary>Owns command parsing, discovery, engine invocation, streams, and exit codes.</summary>
internal static class Runner
{
    public static async Task<int> RunAsync(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (CliUsageException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine($"run `{ex.HelpCommand}` for usage.");
            return 2;
        }

        if (options.Command == CliCommand.Help)
        {
            CliOptions.PrintHelp(Console.Out, options.HelpCommand);
            return 0;
        }

        if (options.Command == CliCommand.Version)
        {
            Console.Out.WriteLine($"{ProductInfo.Name} {ProductInfo.Version}");
            return 0;
        }

        if (options.Command == CliCommand.ConfigDoctor)
            return RunConfigDoctor(options);

        string target;
        string? configPath;
        KnipConfig config;
        try
        {
            target = ResolveAnalysisTarget(options.Target, options.HelpCommand);
            configPath = ResolveConfigPath(
                options.ConfigPath, Path.GetDirectoryName(target)!, required: false, options.HelpCommand);
            config = LoadConfig(configPath);
        }
        catch (CliUsageException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine($"run `{ex.HelpCommand}` for usage.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }

        if (options.Production)
            config.Production = true;

        var progress = options.Verbose
            ? new Progress<string>(message => Console.Error.WriteLine(message))
            : null;

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            if (options.Command == CliCommand.Explain)
            {
                var explanation = await KnipEngine.ExplainAsync(
                    config, target, options.Symbol!, progress, cancellation.Token);
                WriteSafetyDiagnostics(explanation.Reliability);
                if (explanation.Error is not null)
                {
                    Console.Error.WriteLine($"error: {explanation.Error}");
                    foreach (var nearby in explanation.NearbyNames)
                        Console.Error.WriteLine($"  {nearby}");
                    return 2;
                }

                ExplanationTextWriter.Write(explanation, target, Console.Out);
                return 0;
            }

            var result = await KnipEngine.RunAsync(config, target, progress, cancellation.Token);
            WriteSafetyDiagnostics(result.Reliability);
            Reporting.Write(options.Format, result, target, Console.Out);
            return result.Findings.Count > 0 && !options.NoFail ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            if (options.Verbose)
                Console.Error.WriteLine(ex);
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static int RunConfigDoctor(CliOptions options)
    {
        try
        {
            var searchDirectory = Directory.GetCurrentDirectory();
            if (options.Target is not null)
            {
                var target = ValidateTarget(options.Target, "dotnet-knip config doctor --help");
                searchDirectory = Path.GetDirectoryName(target)!;
            }

            var path = ResolveConfigPath(
                options.ConfigPath, searchDirectory, required: true, options.HelpCommand)!;
            var validation = ConfigSchemaValidator.Validate(path);
            if (!validation.IsValid)
            {
                Console.Error.WriteLine($"error: invalid config '{path}':");
                foreach (var error in validation.Errors)
                    Console.Error.WriteLine($"  {error}");
                return 2;
            }

            Console.Out.WriteLine($"valid: {path}");
            return 0;
        }
        catch (CliUsageException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine($"run `{ex.HelpCommand}` for usage.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static KnipConfig LoadConfig(string? path)
    {
        if (path is null)
            return new KnipConfig();

        var validation = ConfigSchemaValidator.Validate(path);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                $"invalid config '{path}':{Environment.NewLine}  " +
                string.Join(Environment.NewLine + "  ", validation.Errors));
        return KnipConfig.Load(path);
    }

    private static string ResolveAnalysisTarget(string? requested, string helpCommand)
    {
        if (requested is not null)
            return ValidateTarget(requested, helpCommand);

        var directory = Directory.GetCurrentDirectory();
        var solutions = Directory.GetFiles(directory, "*.sln")
            .Concat(Directory.GetFiles(directory, "*.slnx"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (solutions.Length == 1)
            return Path.GetFullPath(solutions[0]);
        if (solutions.Length > 1)
            throw AmbiguousTarget(solutions, helpCommand);

        var projects = Directory.GetFiles(directory, "*.csproj")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (projects.Length == 1)
            return Path.GetFullPath(projects[0]);
        if (projects.Length > 1)
            throw AmbiguousTarget(projects, helpCommand);

        throw new CliUsageException(
            "no .sln, .slnx, or .csproj target found in the current directory.",
            helpCommand);
    }

    private static CliUsageException AmbiguousTarget(IEnumerable<string> candidates, string helpCommand) => new(
        "target discovery is ambiguous. Pass one target explicitly. Candidates:" +
        Environment.NewLine + string.Join(Environment.NewLine, candidates.Select(Path.GetFileName)),
        helpCommand);

    private static string ValidateTarget(string target, string helpCommand)
    {
        var path = Path.GetFullPath(target);
        var extension = Path.GetExtension(path);
        if (extension is not (".sln" or ".slnx" or ".csproj"))
            throw new CliUsageException(
                $"unsupported target '{target}'. Expected an existing .sln, .slnx, or .csproj file.",
                helpCommand);
        if (!File.Exists(path))
            throw new CliUsageException($"target not found: {path}", helpCommand);
        return path;
    }

    private static string? ResolveConfigPath(
        string? requested, string searchDirectory, bool required, string helpCommand)
    {
        if (requested is not null)
        {
            var explicitPath = Path.GetFullPath(requested);
            if (!File.Exists(explicitPath))
                throw new CliUsageException($"config not found: {explicitPath}", helpCommand);
            return explicitPath;
        }

        var discovered = KnipConfig.Discover(searchDirectory);
        if (discovered is not null)
            return Path.GetFullPath(discovered);
        if (required)
            throw new CliUsageException(
                $"no knip.json found from '{Path.GetFullPath(searchDirectory)}' through its ancestors.",
                helpCommand);
        return null;
    }

    private static void WriteSafetyDiagnostics(Reliability reliability)
    {
        foreach (var diagnostic in reliability.LoadDiagnostics)
            Console.Error.WriteLine($"{(diagnostic.Severity == LoadSeverity.Error ? "error" : "warning")}: {diagnostic.Message}");
        foreach (var warning in reliability.ProductionModeWarnings)
            Console.Error.WriteLine($"warning: {warning}");
        if (reliability.Degraded)
            Console.Error.WriteLine("warning: analysis reliability is degraded; do not use this run for deletion.");
    }
}
