namespace Knip.Cli;

internal sealed class CliOptions
{
    public string? Solution { get; private set; }
    public string? ConfigPath { get; private set; }
    public string? Format { get; private set; }
    public bool Verbose { get; private set; }
    public bool NoFail { get; private set; }
    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    options.ShowHelp = true;
                    break;
                case "-s" or "--solution":
                    options.Solution = Next(args, ref i, arg);
                    break;
                case "-c" or "--config":
                    options.ConfigPath = Next(args, ref i, arg);
                    break;
                case "-f" or "--format":
                    options.Format = Next(args, ref i, arg);
                    break;
                case "-v" or "--verbose":
                    options.Verbose = true;
                    break;
                case "--no-fail":
                    options.NoFail = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                        throw new ArgumentException($"unknown option: {arg}");
                    // First bare argument is treated as the target solution/project.
                    options.Solution ??= arg;
                    break;
            }
        }

        if (options.Format is not null and not ("console" or "json" or "sarif"))
            throw new ArgumentException($"invalid --format '{options.Format}' (expected console|json|sarif)");

        return options;
    }

    private static string Next(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{flag} requires a value");
        return args[++i];
    }

    public static void PrintUsage(TextWriter output)
    {
        output.WriteLine(
            """
            Knip.NET — find unused code across a .NET solution.

            Usage:
              dotnet-knip [target] [options]

            Arguments:
              target                 Path to a .sln/.slnx/.csproj (default: discovered in cwd or knip.json)

            Options:
              -s, --solution <path>  Solution or project to analyze
              -c, --config <path>    Path to knip.json (default: nearest knip.json up the tree)
              -f, --format <fmt>     Output format: console | json | sarif (default: console)
              -v, --verbose          Print per-project progress to stderr
                  --no-fail          Always exit 0, even when unused code is found
              -h, --help             Show this help

            Exit codes:
              0  no unused code (or --no-fail)
              1  unused code found
              2  usage/load error
            """);
    }
}
