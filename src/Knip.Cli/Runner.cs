using Knip.Core;
using Knip.Core.Configuration;
using Knip.Core.Reporting;

namespace Knip.Cli;

/// <summary>
/// CLI surface. Isolated from Program.cs so MSBuild registration completes before any Roslyn
/// MSBuild type is JIT-loaded.
/// </summary>
internal static class Runner
{
    public static async Task<int> RunAsync(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            CliOptions.PrintUsage(Console.Error);
            return 2;
        }

        if (options.ShowHelp)
        {
            CliOptions.PrintUsage(Console.Out);
            return 0;
        }

        var configPath = options.ConfigPath ?? KnipConfig.Discover(Directory.GetCurrentDirectory());
        var config = KnipConfig.Load(configPath);

        var target = options.Solution ?? config.Solution ?? DiscoverTarget();
        if (target is null)
        {
            Console.Error.WriteLine("error: no solution/project found. Pass --solution <path> or set it in knip.json.");
            return 2;
        }
        if (!File.Exists(target))
        {
            Console.Error.WriteLine($"error: target not found: {target}");
            return 2;
        }

        var format = options.Format ?? config.Output.Format;
        var verbose = options.Verbose;

        var progress = verbose
            ? new Progress<string>(message => Console.Error.WriteLine($"  {message}"))
            : null;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var result = await KnipEngine.RunAsync(config, Path.GetFullPath(target), progress, cts.Token);
            ReporterFactory.Create(format).Report(result, Console.Out);

            if (result.Findings.Count > 0 && !options.NoFail)
                return 1; // CI gate: unused code found
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            if (verbose) Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static string? DiscoverTarget()
    {
        var cwd = Directory.GetCurrentDirectory();
        var solutions = Directory.GetFiles(cwd, "*.sln").Concat(Directory.GetFiles(cwd, "*.slnx")).ToArray();
        if (solutions.Length > 0) return solutions[0];
        var projects = Directory.GetFiles(cwd, "*.csproj");
        return projects.Length == 1 ? projects[0] : null;
    }
}
