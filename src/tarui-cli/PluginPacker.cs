using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Tarui.Cli;

/// <summary>
/// Pre-flight checks for <c>tarui plugin pack</c> (design §8.6): validate the plugin
/// layout, cross-check dependency versions, prove every <c>default.json</c> reference
/// exists in <c>schema.json</c>, run the plugin self-tests, then <c>dotnet pack</c> the
/// backend and <c>npm pack</c> the guest-js frontend, confirming the NuGet package
/// carries its <c>permissions/</c> descriptors. This is a validation gate only — it does
/// not publish to any feed and never auto-grants permissions (design §8.3).
/// </summary>
internal static class PluginPacker
{
    private static readonly Regex CsprojValuePattern = new(
        @"<(?<element>PackageId|Version)\s*>\s*(?<value>[^<]+?)\s*</\k<element>>",
        RegexOptions.Compiled);

    /// <summary>
    /// Discovers the plugin layout under <paramref name="pluginRoot"/> and validates its
    /// structural integrity. The expected layout is the one emitted by
    /// <c>tarui plugin init</c>: a single <c>src/*/*.csproj</c>, a <c>permissions/</c>
    /// directory with <c>schema.json</c> and <c>default.json</c>, and a <c>guest-js/package.json</c>.
    /// </summary>
    public static PluginLayout Detect(string pluginRoot)
    {
        if (!Directory.Exists(pluginRoot))
        {
            throw new CliException($"Plugin directory does not exist: {pluginRoot}");
        }

        var srcProjects = Directory.GetFiles(Path.Combine(pluginRoot, "src"), "*.csproj", SearchOption.AllDirectories);
        if (srcProjects.Length != 1)
        {
            throw new CliException(
                $"Expected exactly one plugin project under src/, but found {srcProjects.Length}.");
        }

        var csprojPath = srcProjects[0];
        var csproj = File.ReadAllText(csprojPath);
        var packageId = ReadValue(csproj, "PackageId")
            ?? Path.GetFileNameWithoutExtension(csprojPath);
        var version = ReadValue(csproj, "Version") ?? "0.1.0";

        var permissionsDirectory = Path.Combine(pluginRoot, "permissions");
        EnsureFile(Path.Combine(permissionsDirectory, "schema.json"), "plugin permission schema");
        EnsureFile(Path.Combine(permissionsDirectory, "default.json"), "plugin default permission set");

        var guestPackageJson = Path.Combine(pluginRoot, "guest-js", "package.json");
        EnsureFile(guestPackageJson, "guest-js package");

        return new PluginLayout(
            CsprojPath: csprojPath,
            PackageId: packageId,
            Version: version,
            PermissionsDirectory: permissionsDirectory,
            GuestPackageJson: guestPackageJson);
    }

    /// <summary>
    /// Validates that every identifier referenced by <c>default.json</c> exists in the
    /// plugin's <c>schema.json</c>, and that schema identifiers are well-formed and unique.
    /// Returns a list of validation problems (empty when valid).
    /// </summary>
    public static IReadOnlyList<string> ValidatePermissions(string schemaPath, string defaultPath)
    {
        var schema = Deserialize(schemaPath, TaruiCliJsonContext.Default.PluginPermissionSchemaDto, "permission schema");
        var defaultSet = Deserialize(defaultPath, TaruiCliJsonContext.Default.DefaultPermissionSetDto, "default permission set");
        var errors = new List<string>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in schema?.Permissions ?? [])
        {
            var identifier = permission.Identifier;
            if (string.IsNullOrWhiteSpace(identifier))
            {
                errors.Add("schema.json contains a permission without an identifier.");
                continue;
            }

            if (!identifier.StartsWith("plugin:", StringComparison.Ordinal))
            {
                errors.Add($"Permission identifier '{identifier}' must start with 'plugin:'.");
            }

            if (!seen.Add(identifier))
            {
                errors.Add($"Duplicate permission identifier in schema.json: '{identifier}'.");
            }

            declared.Add(identifier);
        }

        foreach (var reference in defaultSet?.Permissions ?? [])
        {
            if (!declared.Contains(reference))
            {
                errors.Add($"default.json references '{reference}', which is not declared in schema.json.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Compares the backend package version against the guest-js frontend version,
    /// returning a human-readable problem when they diverge (design §8.6 #3).
    /// </summary>
    public static string? CheckVersionConsistency(PluginLayout layout)
    {
        var guestPackage = Deserialize(layout.GuestPackageJson, TaruiCliJsonContext.Default.GuestPackageDto, "guest-js package");
        var frontendVersion = guestPackage?.Version;
        if (string.IsNullOrWhiteSpace(frontendVersion))
        {
            return "guest-js/package.json must declare a \"version\".";
        }

        if (!string.Equals(layout.Version, frontendVersion, StringComparison.Ordinal))
        {
            return $"Backend version '{layout.Version}' does not match guest-js version '{frontendVersion}'.";
        }

        return null;
    }

    /// <summary>Whether the backend project references the Tarui CLI-owned packages (layout sanity).</summary>
    public static bool HasPermissionsContent(PluginLayout layout) =>
        Directory.Exists(layout.PermissionsDirectory) &&
        Directory.EnumerateFiles(layout.PermissionsDirectory).Any();

    private static string? ReadValue(string csproj, string element)
    {
        foreach (Match match in CsprojValuePattern.Matches(csproj))
        {
            if (string.Equals(match.Groups["element"].Value, element, StringComparison.Ordinal))
            {
                return match.Groups["value"].Value;
            }
        }

        return null;
    }

    private static TDto? Deserialize<TDto>(string path, JsonTypeInfo<TDto> typeInfo, string what)
        where TDto : class
    {
        TDto? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(File.ReadAllText(path), typeInfo);
        }
        catch (JsonException exception)
        {
            throw new CliException($"{what} is not valid JSON: {path}", exception);
        }

        if (parsed is null)
        {
            throw new CliException($"{what} must not be empty: {path}");
        }

        return parsed;
    }

    private static void EnsureFile(string path, string what)
    {
        if (!File.Exists(path))
        {
            throw new CliException($"Missing {what}: {path}");
        }
    }
}

/// <summary>Discovered plugin layout consumed by the pack pre-flight checks.</summary>
internal sealed record PluginLayout(
    string CsprojPath,
    string PackageId,
    string Version,
    string PermissionsDirectory,
    string GuestPackageJson);

/// <summary>JSON-bound recommended minimal permission set (default.json; documentation only).</summary>
internal sealed class DefaultPermissionSetDto
{
    [JsonPropertyName("plugin")] public string? Plugin { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("permissions")] public List<string>? Permissions { get; set; }
}

/// <summary>JSON-bound guest-js package metadata (subset read for version checks).</summary>
internal sealed class GuestPackageDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
}