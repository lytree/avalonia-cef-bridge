namespace Tarui.Contracts;

/// <summary>
/// Static schema metadata for the updater contract. <see cref="SchemaVersion"/> gates cross-version
/// compatibility so a producer (release server) and consumer (running app) never misread each
/// other's manifests; a mismatch is treated as a verification failure rather than a silent parse.
/// </summary>
public static class UpdateContracts
{
    /// <summary>The current manifest schema version understood by this build.</summary>
    public const int SchemaVersion = 1;
}

/// <summary>
/// The signed update manifest. <see cref="Signature"/> is a base64 ECDSA (P-384 / SHA-384) signature
/// over the deterministic canonical form of <see cref="SchemaVersion"/>, <see cref="Version"/>,
/// <see cref="Files"/> and the sorted <see cref="Sha256"/> table. The app verifies the signature with
/// a public key injected at startup and separately revalidates each downloaded file's SHA-256 before
/// staging it, so a single tampered manifest, signature or blob is rejected before any apply step.
/// </summary>
public sealed record UpdateManifest(
    int SchemaVersion,
    string Version,
    string[] Files,
    Dictionary<string, string> Sha256,
    string Signature);

/// <summary>
/// Outcome of <c>plugin:updater|check</c>. <see cref="UpdateAvailable"/> is only reported after the
/// manifest signature and schema verifications pass AND the target version is strictly newer than
/// the current one. Any verification failure sets <see cref="Error"/> instead, so a tampered or
/// misconfigured update is never presented as "no update". <see cref="Version"/> carries the target
/// version when an update is available.
/// </summary>
public sealed record UpdateCheckResult(bool UpdateAvailable, string? Version, string? Error);

/// <summary>
/// Outcome of <c>plugin:updater|download</c>. <see cref="Succeeded"/> is true only when the manifest
/// signature verifies and every advertised file downloads and matches its declared SHA-256. Downloaded
/// blobs are staged under the app data directory; the running installation directory is never touched.
/// </summary>
public sealed record UpdateDownloadResult(bool Succeeded, string? Error);

/// <summary>
/// Payload of the reserved <c>updater://status</c> event. <see cref="Phase"/> is a machine-readable
/// stage (<c>check-success</c>, <c>download-start</c>, <c>download-progress</c>, <c>download-success</c>,
/// <c>verification-failed</c>, <c>check-failed</c>, <c>download-failed</c>); <see cref="Version"/>, <see cref="File"/>
/// and <see cref="Error"/> carry optional context. Delivery is gated by per-window receive capability.
/// </summary>
public sealed record UpdaterStatus(string Phase, string? Version = null, string? File = null, string? Error = null);