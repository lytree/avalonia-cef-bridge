using Tarui.Contracts;

namespace Tarui.Plugins.Updater;

/// <summary>
/// Drives the updater lifecycle. <c>check</c> fetches the signed update manifest, verifies the ECDSA
/// signature and SHA-256 integrity chain, and reports whether a strictly newer version is available.
/// <c>download</c> repeats the verification, then fetches each advertised file and stages it (with per-file
/// SHA-256 revalidation) under the app data directory. <c>apply</c> installs a previously-staged, verified
/// bundle using the platform install strategy; none of these touch the running installation in place, and
/// restart is left to the caller so the host can exit cleanly.
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

    /// <summary>
    /// Applies a previously-staged, verified bundle (see <see cref="DownloadAsync"/>'s staging path) using the
    /// platform's install strategy, emitting <c>apply-start</c>/<c>apply-success</c>/<c>apply-failed</c> status.
    /// Returns <see langword="true"/>-succeeded only when the bundle was actually applied; unsupported bundles or
    /// platforms surface an explicit error rather than a silent no-op. Restart is left to the caller.
    /// </summary>
    ValueTask<UpdateApplyResult> ApplyAsync(UpdateApplyOptions options, CancellationToken cancellationToken);
}