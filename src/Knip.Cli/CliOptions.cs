namespace Knip.Cli;

internal enum CliCommand
{
    Help,
    Version,
    Analyze,
    Explain,
    ConfigDoctor,
}

internal enum ReportFormat
{
    Console,
    Json,
    Sarif,
}

internal sealed class CliUsageException(string message, string helpCommand) : Exception(message)
{
    public string HelpCommand { get; } = helpCommand;
}

internal sealed class CliOptions
{
    public CliCommand Command { get; private set; }
    public string? Target { get; private set; }
    public string? ConfigPath { get; private set; }
    public string? Symbol { get; private set; }
    public ReportFormat Format { get; private set; } = ReportFormat.Console;
    public bool Production { get; private set; }
    public bool NoFail { get; private set; }
    public bool Verbose { get; private set; }
    public string HelpCommand { get; private set; } = "dotnet-knip --help";

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
            return new CliOptions { Command = CliCommand.Help };

        if (args[0] == "--help")
        {
            RequireOnly(args, "--help", "dotnet-knip --help");
            return new CliOptions { Command = CliCommand.Help };
        }

        if (args[0] == "--version")
        {
            RequireOnly(args, "--version", "dotnet-knip --help");
            return new CliOptions { Command = CliCommand.Version };
        }

        return args[0] switch
        {
            "analyze" => ParseCommand(args, CliCommand.Analyze, 1, "dotnet-knip analyze --help"),
            "explain" => ParseCommand(args, CliCommand.Explain, 1, "dotnet-knip explain --help"),
            "config" => ParseConfig(args),
            _ => throw new CliUsageException(
                $"unknown command '{args[0]}'. Expected analyze, explain, or config.",
                "dotnet-knip --help"),
        };
    }

    private static CliOptions ParseConfig(string[] args)
    {
        if (args.Length == 2 && args[1] == "--help")
            return new CliOptions { Command = CliCommand.Help, HelpCommand = "dotnet-knip config --help" };

        if (args.Length < 2 || args[1] != "doctor")
            throw new CliUsageException("config requires the doctor command.", "dotnet-knip config --help");

        return ParseCommand(args, CliCommand.ConfigDoctor, 2, "dotnet-knip config doctor --help");
    }

    private static CliOptions ParseCommand(
        string[] args, CliCommand command, int start, string helpCommand)
    {
        if (args.Skip(start).Contains("--help", StringComparer.Ordinal))
        {
            if (args.Length == start + 1 && args[start] == "--help")
                return new CliOptions { Command = CliCommand.Help, HelpCommand = helpCommand };
            throw new CliUsageException("--help cannot be combined with operational arguments.", helpCommand);
        }

        var options = new CliOptions { Command = command, HelpCommand = helpCommand };
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = start; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (options.Target is not null)
                    throw new CliUsageException($"unexpected extra positional argument '{arg}'.", helpCommand);
                options.Target = arg;
                continue;
            }

            if (!seen.Add(arg))
                throw new CliUsageException($"duplicate option '{arg}'.", helpCommand);

            switch (arg)
            {
                case "--config":
                    options.ConfigPath = Next(args, ref i, arg, helpCommand);
                    break;
                case "--production" when command is CliCommand.Analyze or CliCommand.Explain:
                    options.Production = true;
                    break;
                case "--verbose" when command is CliCommand.Analyze or CliCommand.Explain:
                    options.Verbose = true;
                    break;
                case "--format" when command == CliCommand.Analyze:
                    options.Format = ParseFormat(Next(args, ref i, arg, helpCommand), helpCommand);
                    break;
                case "--no-fail" when command == CliCommand.Analyze:
                    options.NoFail = true;
                    break;
                case "--symbol" when command == CliCommand.Explain:
                    options.Symbol = Next(args, ref i, arg, helpCommand);
                    break;
                default:
                    throw new CliUsageException($"unknown option '{arg}' for this command.", helpCommand);
            }
        }

        if (command == CliCommand.Explain && options.Symbol is null)
            throw new CliUsageException("explain requires --symbol <finding-id-or-display-name>.", helpCommand);

        return options;
    }

    private static ReportFormat ParseFormat(string value, string helpCommand) => value switch
    {
        "console" => ReportFormat.Console,
        "json" => ReportFormat.Json,
        "sarif" => ReportFormat.Sarif,
        _ => throw new CliUsageException(
            $"invalid --format '{value}'. Expected console, json, or sarif.", helpCommand),
    };

    private static string Next(string[] args, ref int i, string option, string helpCommand)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new CliUsageException($"{option} requires a value.", helpCommand);
        return args[++i];
    }

    private static void RequireOnly(string[] args, string option, string helpCommand)
    {
        if (args.Length != 1)
            throw new CliUsageException($"{option} cannot be combined with other arguments.", helpCommand);
    }

    public static void PrintHelp(TextWriter output, string helpCommand)
    {
        switch (helpCommand)
        {
            case "dotnet-knip analyze --help": PrintAnalyzeHelp(output); break;
            case "dotnet-knip explain --help": PrintExplainHelp(output); break;
            case "dotnet-knip config --help": PrintConfigHelp(output); break;
            case "dotnet-knip config doctor --help": PrintDoctorHelp(output); break;
            default: PrintTopLevelHelp(output); break;
        }
    }

    private static void PrintTopLevelHelp(TextWriter output) => output.WriteLine(
        """
        Knip.NET finds removable code and references in .NET solutions.

        Usage:
          dotnet-knip <command> [arguments] [options]
          dotnet-knip --version

        Commands:
          analyze        Analyze a solution or project.
          explain        Explain one graph symbol.
          config doctor  Validate knip.json without loading a target.

        Run `dotnet-knip <command> --help` for command options and examples.

        Examples:
          dotnet-knip analyze
          dotnet-knip analyze MyApp.slnx --format json --no-fail
          dotnet-knip explain MyApp.slnx --symbol "MyApp.Services.Worker.Run()"
          dotnet-knip config doctor
        """);

    private static void PrintAnalyzeHelp(TextWriter output) => output.WriteLine(
        """
        Usage:
          dotnet-knip analyze [target] [options]

        Arguments:
          target              Existing .sln, .slnx, or .csproj file. Strictly discovered when omitted.

        Options:
          --config <path>     Use this exact knip.json file.
          --production        Include code reachable only from tests.
          --format <format>   console, json, or sarif. Default: console.
          --no-fail           Change only a findings exit from 1 to 0.
          --verbose           Write progress to stderr.
          --help              Show this help.

        Exit codes: 0 clean, 1 findings, 2 error, 130 cancelled.

        Examples:
          dotnet-knip analyze
          dotnet-knip analyze MyApp.slnx --format json --no-fail
          dotnet-knip analyze src/MyApp.csproj --config config/knip.json --production
        """);

    private static void PrintExplainHelp(TextWriter output) => output.WriteLine(
        """
        Usage:
          dotnet-knip explain [target] --symbol <finding-id-or-display-name> [options]

        Options:
          --symbol <value>    Exact k1_ finding ID or fully qualified Roslyn display name. Required.
          --config <path>     Use this exact knip.json file.
          --production        Explain production and test reachability separately.
          --verbose           Write progress to stderr.
          --help              Show this help.

        Exit codes: 0 found, 2 error, 130 cancelled.

        Examples:
          dotnet-knip explain MyApp.slnx --symbol k1_0123456789
          dotnet-knip explain --symbol "MyApp.Services.Worker.Run()" --production
        """);

    private static void PrintConfigHelp(TextWriter output) => output.WriteLine(
        """
        Usage:
          dotnet-knip config doctor [target] [--config <path>]

        Commands:
          doctor  Validate one knip.json against the bundled schema.

        Examples:
          dotnet-knip config doctor
        """);

    private static void PrintDoctorHelp(TextWriter output) => output.WriteLine(
        """
        Usage:
          dotnet-knip config doctor [target] [--config <path>]

        Arguments:
          target           Optional existing .sln, .slnx, or .csproj used only as the discovery location.

        Options:
          --config <path>  Validate this exact knip.json file.
          --help           Show this help.

        Exit codes: 0 valid, 2 missing or invalid.

        Examples:
          dotnet-knip config doctor
          dotnet-knip config doctor MyApp.slnx
          dotnet-knip config doctor --config config/knip.json
        """);
}
