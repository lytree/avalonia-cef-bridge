using System.Text.Json.Serialization;

namespace Tarui.Cli;

/// <summary>Normalized tarui.app.json (build-time manifest consumed by the CLI).</summary>
internal sealed record AppManifest(
    AppManifestProduct Product,
    AppManifestBuild Build,
    AppManifestBundle Bundle,
    AppManifestApp? App);

internal sealed record AppManifestProduct(string Name, string Version, string Identifier);

internal sealed record AppManifestBuild(
    string? Frontend,
    string? BeforeDevCommand,
    string? DevUrl,
    string? BeforeBuildCommand,
    string FrontendDist,
    string? DesktopProject);

internal sealed record AppManifestBundle(
    IReadOnlyList<string> Targets,
    string? Icon,
    string? ShortDescription,
    AppManifestMsix? Msix,
    AppManifestMacOs? MacOs);

/// <summary>MSIX bundle configuration (design §5.5 / W5). Signing stays optional:
/// the packer emits a structurally valid package, and Authenticode signing runs when
/// a certificate + signtool are supplied (cert procurement is the W5 prerequisite).</summary>
internal sealed record AppManifestMsix(
    string? Publisher,
    string? CertificatePath,
    string? CertificatePassword,
    string? TimeStamperUrl);

internal sealed record AppManifestApp(IReadOnlyList<string> Capabilities);

/// <summary>
/// macOS application bundle configuration (W5). The CLI uses <see cref="Schemes"/> to drive
/// <c>CFBundleURLTypes</c> in <c>Info.plist</c>; <see cref="BundleId"/> / <see cref="ExecutableName"/>
/// / <see cref="MinimumSystemVersion"/> override the default derivations from <c>product</c>.
/// Notarization and stapling stay out of scope (Windows-first CLI; macOS produces an unsigned
/// unsigned .app bundle that an operator can notarize before distribution).
/// </summary>
internal sealed record AppManifestMacOs(
    string? BundleId,
    string? ExecutableName,
    string? MinimumSystemVersion,
    IReadOnlyList<string> Schemes);

/// <summary>JSON-bound DTO (camelCase, source generated). Unknown properties such as $schema are ignored.</summary>
internal sealed class AppManifestDto
{
    [JsonPropertyName("product")] public AppManifestProductDto? Product { get; set; }
    [JsonPropertyName("build")] public AppManifestBuildDto? Build { get; set; }
    [JsonPropertyName("bundle")] public AppManifestBundleDto? Bundle { get; set; }
    [JsonPropertyName("app")] public AppManifestAppDto? App { get; set; }
}

internal sealed class AppManifestProductDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("identifier")] public string? Identifier { get; set; }
}

internal sealed class AppManifestBuildDto
{
    [JsonPropertyName("frontend")] public string? Frontend { get; set; }
    [JsonPropertyName("beforeDevCommand")] public string? BeforeDevCommand { get; set; }
    [JsonPropertyName("devUrl")] public string? DevUrl { get; set; }
    [JsonPropertyName("beforeBuildCommand")] public string? BeforeBuildCommand { get; set; }
    [JsonPropertyName("frontendDist")] public string? FrontendDist { get; set; }
    [JsonPropertyName("desktopProject")] public string? DesktopProject { get; set; }
}

internal sealed class AppManifestBundleDto
{
    [JsonPropertyName("targets")] public List<string>? Targets { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("shortDescription")] public string? ShortDescription { get; set; }
    [JsonPropertyName("msix")] public AppManifestMsixDto? Msix { get; set; }
    [JsonPropertyName("macOS")] public AppManifestMacOsDto? MacOs { get; set; }
}

internal sealed class AppManifestMacOsDto
{
    [JsonPropertyName("bundleId")] public string? BundleId { get; set; }
    [JsonPropertyName("executableName")] public string? ExecutableName { get; set; }
    [JsonPropertyName("minimumSystemVersion")] public string? MinimumSystemVersion { get; set; }
    [JsonPropertyName("schemes")] public List<string>? Schemes { get; set; }
}

internal sealed class AppManifestMsixDto
{
    [JsonPropertyName("publisher")] public string? Publisher { get; set; }
    [JsonPropertyName("certificate")] public AppManifestMsixCertificateDto? Certificate { get; set; }
}

internal sealed class AppManifestMsixCertificateDto
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    [JsonPropertyName("timeStamperUrl")] public string? TimeStamperUrl { get; set; }
}

internal sealed class AppManifestAppDto
{
    [JsonPropertyName("capabilities")] public List<string>? Capabilities { get; set; }
}

/// <summary>Loads tarui.app.json into a normalized <see cref="AppManifest"/>.</summary>
internal static class AppManifestLoader
{
    public static AppManifest Parse(string json)
    {
        AppManifestDto? dto;
        try
        {
            dto = System.Text.Json.JsonSerializer.Deserialize(
                json,
                TaruiCliJsonContext.Default.AppManifestDto);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new CliException($"tarui.app.json is not valid JSON: {exception.Message}");
        }

        if (dto is null)
        {
            throw new CliException("tarui.app.json is empty.");
        }

        return new AppManifest(
            new AppManifestProduct(
                dto.Product?.Name ?? string.Empty,
                dto.Product?.Version ?? string.Empty,
                dto.Product?.Identifier ?? string.Empty),
            new AppManifestBuild(
                dto.Build?.Frontend,
                dto.Build?.BeforeDevCommand,
                dto.Build?.DevUrl,
                dto.Build?.BeforeBuildCommand,
                dto.Build?.FrontendDist ?? string.Empty,
                dto.Build?.DesktopProject),
            new AppManifestBundle(
                dto.Bundle?.Targets ?? [],
                dto.Bundle?.Icon,
                dto.Bundle?.ShortDescription,
                ToMsix(dto.Bundle?.Msix),
                ToMacOs(dto.Bundle?.MacOs)),
            dto.App is null
                ? null
                : new AppManifestApp(dto.App.Capabilities ?? []));
    }

    private static AppManifestMsix? ToMsix(AppManifestMsixDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        return new AppManifestMsix(
            dto.Publisher,
            dto.Certificate?.Path,
            dto.Certificate?.Password,
            dto.Certificate?.TimeStamperUrl);
    }

    private static AppManifestMacOs? ToMacOs(AppManifestMacOsDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        // Filter out whitespace-only entries early so the CLI surface stays deterministic; the
        // downstream builder re-validates each token before injection.
        var schemes = (dto.Schemes ?? [])
            .Where(scheme => !string.IsNullOrWhiteSpace(scheme))
            .Select(scheme => scheme.Trim())
            .ToArray();
        return new AppManifestMacOs(
            string.IsNullOrWhiteSpace(dto.BundleId) ? null : dto.BundleId.Trim(),
            string.IsNullOrWhiteSpace(dto.ExecutableName) ? null : dto.ExecutableName.Trim(),
            string.IsNullOrWhiteSpace(dto.MinimumSystemVersion) ? null : dto.MinimumSystemVersion.Trim(),
            schemes);
    }

    public static AppManifest Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new CliException($"Manifest not found: {manifestPath}");
        }

        return Parse(File.ReadAllText(manifestPath));
    }
}
