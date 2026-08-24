using Microsoft.Extensions.Configuration;

namespace Tarui.Shell;

/// <summary>
/// The resolved updater settings. <see cref="ManifestUri"/> points at the signed JSON manifest,
/// <see cref="PublicKeyB64"/> carries the base64 DER SubjectPublicKeyInfo used to verify it,
/// <see cref="CurrentVersion"/> is the baseline the target version is compared against, and
/// <see cref="StagingDir"/> is the isolated directory where verified blobs are staged (never the
/// running installation). <see cref="MaxManifestBytes"/>, <see cref="MaxFileBytes"/> and
/// <see cref="MaxTotalBytes"/> cap the resources an updater transaction may consume so a malicious
/// or misconfigured manifest cannot exhaust memory or disk before its signature is checked.
/// <see langword="null"/> means the updater is not configured.
/// </summary>
public sealed record UpdaterSettings(
    Uri ManifestUri,
    string PublicKeyB64,
    string CurrentVersion,
    string StagingDir,
    long MaxManifestBytes = 1L * 1024 * 1024,
    long MaxFileBytes = 200L * 1024 * 1024,
    long MaxTotalBytes = 1L * 1024 * 1024 * 1024,
    bool AllowInsecureHttp = false);

/// <summary>
/// Reads and validates the updater configuration from <c>Tarui:Application:Updater</c>. A missing or
/// invalid section yields <see langword="null"/> so check/download report <c>updater-not-configured</c>
/// rather than crashing. <c>CurrentVersion</c> defaults to the packaged <c>0.1.0</c>; the staging
/// directory resolves under the per-user local app data directory by default.
/// </summary>
internal static class UpdaterConfiguration
{
    public static UpdaterSettings? ReadSettings(IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return null;
        }

        var section = configuration.GetSection("Tarui:Application:Updater");
        if (!section.Exists())
        {
            return null;
        }

        var manifestUrl = section["ManifestUrl"];
        var publicKey = section["PublicKey"];
        if (string.IsNullOrWhiteSpace(manifestUrl) ||
            string.IsNullOrWhiteSpace(publicKey) ||
            !Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifestUri) ||
            (manifestUri.Scheme != Uri.UriSchemeHttps && manifestUri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        if (manifestUri.Scheme == Uri.UriSchemeHttp &&
            !TryGetBool(section, "AllowInsecureHttp", defaultValue: false, out _))
        {
            // HTTP is opt-in only; reject the section unless the operator explicitly toggles it.
            return null;
        }

        var currentVersion = section["CurrentVersion"];
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            currentVersion = "0.1.0";
        }

        var stagingDir = section["StagingDir"];
        if (string.IsNullOrWhiteSpace(stagingDir))
        {
            stagingDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "tarui.net",
                "updater",
                "staging");
        }

        return new UpdaterSettings(
            ManifestUri: manifestUri,
            PublicKeyB64: publicKey,
            CurrentVersion: currentVersion,
            StagingDir: stagingDir,
            MaxManifestBytes: ReadBytes(section, "MaxManifestBytes", defaultValue: 1L * 1024 * 1024),
            MaxFileBytes: ReadBytes(section, "MaxFileBytes", defaultValue: 200L * 1024 * 1024),
            MaxTotalBytes: ReadBytes(section, "MaxTotalBytes", defaultValue: 1L * 1024 * 1024 * 1024),
            AllowInsecureHttp: TryGetBool(section, "AllowInsecureHttp", defaultValue: false, out var insecure) && insecure);
    }

    private static bool TryGetBool(IConfigurationSection section, string key, bool defaultValue, out bool value)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = defaultValue;
            return true;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = defaultValue;
        return false;
    }

    private static long ReadBytes(IConfigurationSection section, string key, long defaultValue)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return long.TryParse(raw, out var parsed) && parsed > 0 ? parsed : defaultValue;
    }
}
