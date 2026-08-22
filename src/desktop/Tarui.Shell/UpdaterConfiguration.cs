using Microsoft.Extensions.Configuration;

namespace Tarui.Shell;

/// <summary>
/// The resolved updater settings. <see cref="ManifestUri"/> points at the signed JSON manifest,
/// <see cref="PublicKeyB64"/> carries the base64 DER SubjectPublicKeyInfo used to verify it,
/// <see cref="CurrentVersion"/> is the baseline the target version is compared against, and
/// <see cref="StagingDir"/> is the isolated directory where verified blobs are staged (never the
/// running installation). <see langword="null"/> means the updater is not configured.
/// </summary>
public sealed record UpdaterSettings(
    Uri ManifestUri,
    string PublicKeyB64,
    string CurrentVersion,
    string StagingDir);

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

        return new UpdaterSettings(manifestUri, publicKey, currentVersion, stagingDir);
    }
}