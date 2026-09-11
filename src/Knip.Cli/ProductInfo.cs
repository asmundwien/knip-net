using System.Reflection;

namespace Knip.Cli;

internal static class ProductInfo
{
    public const string Name = "Knip.NET";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(ProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            throw new InvalidOperationException("Knip.NET assembly informational version is missing");

        var metadata = informational!.IndexOf('+');
        return metadata < 0 ? informational : informational.Substring(0, metadata);
    }
}
