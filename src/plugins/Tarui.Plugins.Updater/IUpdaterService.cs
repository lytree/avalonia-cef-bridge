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
    /// Before installing, the signed manifest persisted by <see cref="DownloadAsync"/> is re-verified and every
    /// staged file is re-hashed, so a blob swapped after download is rejected instead of applied. Returns
    /// <see langword="true"/>-succeeded only when the bundle was actually applied; unsupported bundles or
    /// platforms surface an explicit error rather than a silent no-op. Restart is left to the caller.
    /// </summary>
    ValueTask<UpdateApplyResult> ApplyAsync(UpdateApplyOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Re-verifies a cached signed manifest and applies the configured version policy without any network
    /// access, mirroring Tauri's <c>Updater::restore_update</c>: a previously fetched manifest can be turned
    /// back into a trusted update handle only when its signature still verifies against the configured key.
    /// </summary>
    ValueTask<UpdateRestoreResult> RestoreAsync(UpdateRestoreOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies the signed manifest and every staged file's SHA-256, mirroring Tauri's <c>Update::verify</c>.
    /// The running installation and the network are never touched; a mismatch or missing blob is reported as
    /// an explicit error instead of succeeding.
    /// </summary>
    ValueTask<UpdateVerifyResult> VerifyAsync(UpdateVerifyOptions options, CancellationToken cancellationToken);
}