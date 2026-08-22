using Tarui.Contracts;

namespace Tarui.Plugins.Updater;

/// <summary>
/// Drives the read-only half of the updater. <c>check</c> fetches the signed update manifest, verifies
/// the ECDSA signature and SHA-256 integrity chain, and reports whether a strictly newer version is
/// available. <c>download</c> repeats the verification, then fetches each advertised file and stages it
/// (with per-file SHA-256 revalidation) under the app data directory. Neither command ever touches the
/// running installation or performs an in-place apply; <c>apply</c> is intentionally out of scope until
/// the signing PKI, update server and installer/bootstrapper strategy are verified on real hardware.
/// </summary>
public interface IUpdaterService
{
    /// <summary>
    /// Fetches and verifies the update manifest, reporting whether a strictly newer version exists or
    /// why verification failed. Verification failures are surfaced as an error, never as "no update".
    /// </summary>
    ValueTask<UpdateCheckResult> CheckAsync(EmptyArgs options, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches and verifies the update manifest, then stages every file to the app data staging
    /// directory after revalidating each blob's SHA-256. Returns failure for any hash mismatch or
    /// verification issue without touching the running installation.
    /// </summary>
    ValueTask<UpdateDownloadResult> DownloadAsync(EmptyArgs options, CancellationToken cancellationToken);
}