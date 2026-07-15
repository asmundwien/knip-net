using System.Text.Json;
using Knip.Core;
using Knip.Core.Analysis;
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
        KnipConfig config;
        try
        {
            config = KnipConfig.Load(configPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }

        // WS7: production mode enables via the CLI flag OR the config key (flag never turns it OFF).
        // Applied before --print-config so the printed effective config reflects the CLI override.
        if (options.Production) config.Production = true;

        // Unknown-key warnings (WS8c / L7) surface through the LoadDiagnostics channel during analysis
        // (analyzer → reporter/reliability block), exactly like the plugin warnings. The QUERY commands
        // below (--print-config / --why) bypass that channel, so for them we emit the same warnings LOUD
        // on stderr here so config typos never silently no-op. stdout stays clean (J6).
        if (options.PrintConfig || options.Why is not null)
            foreach (var warning in config.ValidateKeys())
                Console.Error.WriteLine($"warning: {warning}");

        // --print-config (WS8c / L6): the EFFECTIVE merged config (file over defaults) as JSON to stdout,
        // exit 0. No analysis. SourcePath is [JsonIgnore] (loader-internal) so it never appears.
        if (options.PrintConfig)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(config, KnipConfig.JsonOptions));
            return 0;
        }

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
            // --why (WS8c / L5) captures provenance so we can trace a symbol; a plain run does NOT
            // (memory unchanged). It is a QUERY, not a gate: prose report to stdout, always exit 0.
            var result = await KnipEngine.RunAsync(
                config, Path.GetFullPath(target), progress, cts.Token, captureProvenance: options.Why is not null);

            // WS7: production-mode warnings (e.g. zero test projects detected) are LOUD on stderr
            // regardless of -v — they change the meaning of the results. Machine output stays clean
            // on stdout (J6); the same warnings are in reliability.productionModeWarnings for consumers.
            foreach (var warning in result.Reliability.ProductionModeWarnings)
                Console.Error.WriteLine($"warning: {warning}");

            if (options.Why is not null)
            {
                Console.Out.WriteLine(WhyService.Explain(result, options.Why));
                return 0; // a query never gates CI.
            }

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
