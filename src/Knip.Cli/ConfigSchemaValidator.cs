using System.Reflection;
using System.Text.Json;
using Json.Schema;
using Knip.Core.Plugins;

namespace Knip.Cli;

internal sealed class ConfigValidationResult
{
    public ConfigValidationResult(bool isValid, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public bool IsValid { get; }
    public IReadOnlyList<string> Errors { get; }
}

internal static class ConfigSchemaValidator
{
    private const string ResourceName = "Knip.Cli.Schemas.knip.config.schema.json";
    private static readonly Lazy<JsonSchema> BundledSchema = new(LoadSchema);

    public static ConfigValidationResult Validate(string path)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            var location = ex.LineNumber is null
                ? string.Empty
                : $" at line {ex.LineNumber + 1}, byte {ex.BytePositionInLine + 1}";
            return new ConfigValidationResult(false, [$"invalid JSON{location}: {ex.Message}"]);
        }

        using (document)
        {
            var evaluation = BundledSchema.Value.Evaluate(
                document.RootElement,
                new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (evaluation.IsValid)
                return new ConfigValidationResult(true, []);

            var errors = evaluation.Details
                .Where(detail => detail.HasErrors)
                .SelectMany(detail => detail.Errors!.Select(error =>
                    $"{FormatPath(detail.InstanceLocation.ToString())}: {error.Value}"))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return new ConfigValidationResult(false, errors);
        }
    }

    private static JsonSchema LoadSchema()
    {
        var assembly = typeof(ConfigSchemaValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded config schema '{ResourceName}' is missing");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        EnsurePluginParity(text);
        return JsonSchema.FromText(text);
    }

    private static void EnsurePluginParity(string schemaText)
    {
        using var document = JsonDocument.Parse(schemaText);
        var root = document.RootElement;
        var pluginProperties = root.GetProperty("properties").GetProperty("plugins").GetProperty("properties");
        var schemaIds = pluginProperties.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var registryIds = PluginRegistry.All.Select(plugin => plugin.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (!schemaIds.SequenceEqual(registryIds, StringComparer.Ordinal))
            throw new InvalidOperationException("embedded config schema plugin ids do not match PluginRegistry");

        var definitions = root.GetProperty("$defs");
        foreach (var plugin in PluginRegistry.All)
        {
            var reference = pluginProperties.GetProperty(plugin.Id).GetProperty("$ref").GetString()!;
            var definitionName = reference.Substring(reference.LastIndexOf('/') + 1);
            var schemaKeys = definitions.GetProperty(definitionName).GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .Where(name => name != "enabled")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var registryKeys = plugin.SettingKeys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (!schemaKeys.SequenceEqual(registryKeys, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"embedded config schema settings for '{plugin.Id}' do not match PluginRegistry");
        }
    }

    private static string FormatPath(string pointer)
    {
        if (string.IsNullOrEmpty(pointer)) return "$";
        return pointer.TrimStart('/').Replace("/", ".");
    }
}
