using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tarui.Cli;

/// <summary>
/// Merges every referenced plugin's <c>permissions/schema.json</c> into a single
/// application-level permission schema written into the publish output at
/// <c>schemas/permissions.schema.json</c> (design §8.3). This is a validation aid for
/// IDE completion and startup checks only: <c>capabilities/*.json</c> remain the sole
/// runtime authorization source and plugin descriptors are never auto-granted.
/// </summary>
internal static class SchemaSynthesizer
{
    private const string PermissionsRoot = "permissions";
    private static readonly string OutputRelativePath = Path.Combine("schemas", "permissions.schema.json");

    /// <summary>Collects and merges plugin permission schemas from the publish output.</summary>
    public static SynthesizedPermissionSchemaDto Synthesize(string binDir)
    {
        var pluginsRoot = Path.Combine(binDir, PermissionsRoot);
        var schemaFiles = Directory.Exists(pluginsRoot)
            ? Directory.GetDirectories(pluginsRoot)
                .SelectMany(directory => Directory.GetFiles(directory, "schema.json", SearchOption.TopDirectoryOnly))
            : Array.Empty<string>();

        var plugins = new List<PluginPermissionSchemaDto>();
        foreach (var file in schemaFiles)
        {
            plugins.Add(ParseSchema(file));
        }

        plugins.Sort(static (a, b) => string.CompareOrdinal(a.Plugin, b.Plugin));

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in plugins.SelectMany(static plugin => plugin.Permissions ?? []))
        {
            if (!string.IsNullOrWhiteSpace(permission.Identifier) && !identifiers.Add(permission.Identifier))
            {
                throw new CliException(
                    $"Duplicate plugin permission identifier in schema synthesis: '{permission.Identifier}'. " +
                    "Plugin permission identifiers must be globally unique.");
            }
        }

        return new SynthesizedPermissionSchemaDto { Version = "1", Plugins = plugins };
    }

    /// <summary>Writes the synthesized schema into the publish output and returns its path.</summary>
    public static string Write(string binDir, SynthesizedPermissionSchemaDto schema)
    {
        var outputPath = Path.GetFullPath(Path.Combine(binDir, OutputRelativePath));
        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new CliException($"Cannot determine the output directory for '{outputPath}'.");
        Directory.CreateDirectory(outputDirectory);
        var json = JsonSerializer.Serialize(schema, TaruiCliJsonContext.Default.SynthesizedPermissionSchemaDto);
        File.WriteAllText(outputPath, json);
        return outputPath;
    }

    private static PluginPermissionSchemaDto ParseSchema(string filePath)
    {
        PluginPermissionSchemaDto? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(
                File.ReadAllText(filePath),
                TaruiCliJsonContext.Default.PluginPermissionSchemaDto);
        }
        catch (JsonException exception)
        {
            throw new CliException($"Plugin permission schema is not valid JSON: {filePath}", exception);
        }

        if (string.IsNullOrWhiteSpace(parsed?.Plugin))
        {
            parsed ??= new PluginPermissionSchemaDto();
            var directory = Path.GetFileName(Path.GetDirectoryName(filePath) ?? filePath) ?? filePath;
            parsed.Plugin = directory;
        }

        return parsed;
    }
}

/// <summary>JSON-bound application-level permission schema (source generated, camelCase).</summary>
internal sealed class SynthesizedPermissionSchemaDto
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("plugins")] public List<PluginPermissionSchemaDto>? Plugins { get; set; }
}

/// <summary>JSON-bound per-plugin permission descriptor (mirrors the plugin <c>schema.json</c>).</summary>
internal sealed class PluginPermissionSchemaDto
{
    [JsonPropertyName("plugin")] public string? Plugin { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("permissions")] public List<PluginPermissionDescriptorDto>? Permissions { get; set; }
    [JsonPropertyName("events")] public List<string>? Events { get; set; }
    [JsonPropertyName("default")] public List<string>? Default { get; set; }
}

/// <summary>JSON-bound plugin permission descriptor.</summary>
internal sealed class PluginPermissionDescriptorDto
{
    [JsonPropertyName("identifier")] public string? Identifier { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("scope")] public object? Scope { get; set; }
}