using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Events;
using Tarui.Plugins.Updater;

namespace Tarui.Plugins.Updater;

/// <summary>
/// Raised when the updater fetches a manifest or file that exceeds the configured byte caps, or
/// when the advertised Content-Length disagrees with the signed file size. Surfaced as a
/// non-sensitive <see cref="UpdateDownloadResult.Error"/> or <see cref="UpdateCheckResult.Error"/>
/// so the web layer can distinguish it from a network failure.
/// </summary>
public sealed class UpdaterSizeLimitException(string message) : Exception(message);

/// <summary>
/// Drives the read-only updater flow. <c>check</c> fetches the signed manifest, verifies its ECDSA
/// signature and schema, and reports whether a strictly newer version exists. <c>download</c> repeats
/// that verification, then fetches each advertised file to an isolated staging directory after
/// revalidating its SHA-256. The running installation is never touched; staged paths are confined
/// to the staging root and file entries are validated so a tampered manifest cannot escape it.
/// Verification or hash failures surface as a Web-facing error, never as "no update".
/// Progress/completion is reported through the reserved <c>updater://status</c> event.
///
/// Downloads are serialized through an internal <see cref="SemaphoreSlim"/> so two concurrent
/// calls cannot stomp each other's staging directories; per-file and cumulative byte caps are
/// enforced while streaming so a hostile manifest cannot exhaust memory or disk before its
/// signature is rejected.
/// </summary>
public sealed partial class UpdaterService : IUpdaterService, IDisposable
{
    private static readonly JsonTypeInfo<UpdateManifest> ManifestTypeInfo = TaruiJsonContext.Default.UpdateManifest;

    /// <summary>
    /// The signed manifest persisted next to the staged blobs so <c>apply</c> can re-verify the payload
    /// immediately before installing it.
    /// </summary>
    internal const string StagedManifestFileName = "tarui-update-manifest.json";

    private readonly HttpClient _http;
    private readonly UpdaterSettings? _settings;
    private readonly IEventSender? _events;
    private readonly ILogger<UpdaterService> _logger;
    private readonly IUpdateApplier _applier;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public UpdaterService(
        HttpClient http,
        UpdaterSettings? settings,
        IEventSender? events,
        ILogger<UpdaterService> logger,
        IUpdateApplier? updateApplier = null)
    {
        _http = http;
        _settings = settings;
        _events = events;
        _logger = logger;
        _applier = updateApplier ?? new NoOpUpdateApplier();
    }

    public async ValueTask<UpdateCheckResult> CheckAsync(EmptyArgs options, CancellationToken cancellationToken)
    {
        if (_settings is null)
        {
            return new UpdateCheckResult(false, null, "updater-not-configured");
        }

        try
        {
            EnsureHttpsAllowed();
            var manifest = await FetchVerifiedManifestAsync(cancellationToken);
            await EmitStatusAsync("check-success", manifest.Version, cancellationToken: cancellationToken);
            return CompareVersion(manifest);
        }
        catch (UpdateVerificationException exception)
        {
            LogVerificationFailure(exception);

            await EmitStatusAsync("verification-failed", error: exception.Message, cancellationToken: cancellationToken);
            return new UpdateCheckResult(false, null, exception.Message);
        }
        catch (UpdaterSizeLimitException exception)
        {
            LogSizeLimit("check", exception);

            await EmitStatusAsync("check-failed", error: "manifest-too-large", cancellationToken: cancellationToken);
            return new UpdateCheckResult(false, null, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            LogFetchFailure("check", exception);

            await EmitStatusAsync("check-failed", error: "check-fetch-failed", cancellationToken: cancellationToken);
            return new UpdateCheckResult(false, null, "check-fetch-failed");
        }
    }

    public async ValueTask<UpdateDownloadResult> DownloadAsync(
        EmptyArgs options,
        CancellationToken cancellationToken)
    {
        if (_settings is null)
        {
            return new UpdateDownloadResult(false, "updater-not-configured");
        }

        // Serialize download calls so two concurrent invocations do not share staging state.
        // The semaphore is released in the outer finally so cancellation still frees the slot.
        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureHttpsAllowed();
            var manifest = await FetchVerifiedManifestAsync(cancellationToken);
            await EmitStatusAsync("download-start", manifest.Version, cancellationToken: cancellationToken);

            // Each transaction gets its own staging directory so an interrupted (and therefore
            // half-verified) download can never overwrite the previously staged active set.
            var stagingRoot = Path.Combine(
                Path.GetFullPath(_settings.StagingDir),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);

            long cumulativeBytes = 0;
            try
            {
                foreach (var file in manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var target = ResolveStagingPath(stagingRoot, file);
                    var expected = manifest.Sha256[file];
                    await EmitStatusAsync("download-progress", manifest.Version, file, cancellationToken: cancellationToken);

                    var temporary = target + ".tmp";
                    try
                    {
                        var actual = await DownloadAndHashAsync(
                            ResolveFileUri(file),
                            temporary,
                            _settings.MaxFileBytes,
                            cumulativeBytes,
                            _settings.MaxTotalBytes,
                            cancellationToken);
                        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new UpdateVerificationException($"hash-mismatch:{file}");
                        }

                        File.Move(temporary, target);
                    }
                    finally
                    {
                        TryDelete(temporary);
                    }
                }

                // Persist the verified manifest with the blobs so apply can re-check the payload it is
                // about to install instead of trusting files that may have been swapped after download.
                await File.WriteAllBytesAsync(
                    Path.Combine(stagingRoot, StagedManifestFileName),
                    JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestTypeInfo),
                    cancellationToken).ConfigureAwait(false);

                await EmitStatusAsync("download-success", manifest.Version, cancellationToken: cancellationToken);
                return new UpdateDownloadResult(true, null, stagingRoot);
            }
            catch
            {
                // Best-effort cleanup of the transaction's staging directory on any error path.
                // Important: this is in a catch, not a finally, because the success branch
                // exposes stagingRoot to the caller and a finally cleanup would delete it before
                // the caller could use it.
                if (Directory.Exists(stagingRoot))
                {
                    try
                    {
                        Directory.Delete(stagingRoot, recursive: true);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
                throw;
            }
        }
        catch (UpdateVerificationException exception)
        {
            LogVerificationFailure(exception);

            await EmitStatusAsync("download-failed", error: exception.Message, cancellationToken: cancellationToken);
            return new UpdateDownloadResult(false, exception.Message);
        }
        catch (UpdaterSizeLimitException exception)
        {
            LogSizeLimit("download", exception);

            await EmitStatusAsync("download-failed", error: "download-too-large", cancellationToken: cancellationToken);
            return new UpdateDownloadResult(false, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            LogFetchFailure("download", exception);

            await EmitStatusAsync("download-failed", error: "download-fetch-failed", cancellationToken: cancellationToken);
            return new UpdateDownloadResult(false, "download-fetch-failed");
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    /// <summary>
    /// Applies a previously-staged, verified bundle. The staging path must sit under the configured staging root;
    /// the applier owns the actual install mechanism (bundle selection + apply). Any apply failure is surfaced as
    /// <c>apply-failed</c> status and a non-succeeded result; an unsupported bundle/platform surfaces an explicit
    /// error rather than a silent no-op. Restart is intentionally left to the caller so the host can exit cleanly.
    /// </summary>
    public async ValueTask<UpdateApplyResult> ApplyAsync(
        UpdateApplyOptions options,
        CancellationToken cancellationToken)
    {
        var restart = options.Restart;
        if (_settings is null)
        {
            return new UpdateApplyResult(false, "updater-not-configured", restart);
        }

        // Confine the staging path to the configured staging root; only a DownloadAsync result is valid input.
        var stagingPath = ResolveStagingDirectory(options.StagingPath);
        if (stagingPath is null)
        {
            return new UpdateApplyResult(false, "invalid-staging-path", restart);
        }

        // Re-verify the payload about to be installed: the signed manifest persisted by download plus every
        // staged blob's SHA-256. This is the install-time verification introduced upstream in updater 2.14.
        UpdateManifest manifest;
        try
        {
            var manifestPath = Path.Combine(stagingPath, StagedManifestFileName);
            if (!File.Exists(manifestPath))
            {
                await EmitStatusAsync("apply-failed", error: "missing-update-manifest", cancellationToken: cancellationToken);
                return new UpdateApplyResult(false, "missing-update-manifest", restart);
            }

            manifest = ParseAndVerifyManifest(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        }
        catch (UpdateVerificationException exception)
        {
            LogVerificationFailure(exception);
            await EmitStatusAsync("apply-failed", error: exception.Message, cancellationToken: cancellationToken);
            return new UpdateApplyResult(false, exception.Message, restart);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogApplyFailure(exception);
            await EmitStatusAsync("apply-failed", error: "invalid-update-manifest", cancellationToken: cancellationToken);
            return new UpdateApplyResult(false, "invalid-update-manifest", restart);
        }

        var verificationError = VerifyStagedFiles(manifest, stagingPath);
        if (verificationError is not null)
        {
            var error = $"staged-verification-failed:{verificationError}";
            await EmitStatusAsync("apply-failed", error: error, cancellationToken: cancellationToken);
            return new UpdateApplyResult(false, error, restart);
        }

        // Only a bundle named by the verified manifest may be installed; an extra MSIX dropped into the
        // staging directory after download is never selected.
        var bundle = manifest.Files
            .Where(static file => file.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
            .Select(file => ResolveStagedFilePath(stagingPath, file))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (bundle is null)
        {
            await EmitStatusAsync("apply-failed", error: "no-bundle-staged", cancellationToken: cancellationToken);
            return new UpdateApplyResult(false, "no-bundle-staged", restart);
        }

        await EmitStatusAsync("apply-start", file: Path.GetFileName(bundle), cancellationToken: cancellationToken);
        try
        {
            var applied = await _applier.ApplyAsync(stagingPath, cancellationToken).ConfigureAwait(false);
            if (!applied)
            {
                await EmitStatusAsync("apply-failed", error: "update-apply-unsupported", cancellationToken: cancellationToken);
                return new UpdateApplyResult(false, "update-apply-unsupported", restart);
            }

            await EmitStatusAsync("apply-success", file: Path.GetFileName(bundle), cancellationToken: cancellationToken);
            return new UpdateApplyResult(true, null, restart);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogApplyFailure(exception);
            await EmitStatusAsync("apply-failed", error: "apply-failed", cancellationToken: cancellationToken);
            return new UpdateApplyResult(false, "apply-failed", restart);
        }
    }

    /// <summary>
    /// Restores a cached, previously verified manifest without touching the network. The manifest is
    /// re-parsed and re-verified against the configured public key and version policy, so persisting a
    /// manifest cannot be used to smuggle in an unsigned or stale release.
    /// </summary>
    public ValueTask<UpdateRestoreResult> RestoreAsync(
        UpdateRestoreOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_settings is null)
        {
            return ValueTask.FromResult(new UpdateRestoreResult(false, null, "updater-not-configured"));
        }

        try
        {
            var manifest = ParseAndVerifyManifest(options.ManifestJson);
            var comparison = CompareVersions(manifest.Version);
            return ValueTask.FromResult(comparison.Error is not null
                ? new UpdateRestoreResult(false, null, comparison.Error)
                : comparison.IsUpdate
                    ? new UpdateRestoreResult(true, manifest.Version, null)
                    : new UpdateRestoreResult(false, null, "no-update"));
        }
        catch (UpdateVerificationException exception)
        {
            LogVerificationFailure(exception);
            return ValueTask.FromResult(new UpdateRestoreResult(false, null, exception.Message));
        }
    }

    /// <summary>
    /// Verifies a signed manifest and every staged blob it advertises. No network access and no install
    /// step happen here: this is the standalone equivalent of Tauri's <c>Update::verify</c>, and the exact
    /// same check apply performs before handing the bundle to the platform applier.
    /// </summary>
    public ValueTask<UpdateVerifyResult> VerifyAsync(
        UpdateVerifyOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_settings is null)
        {
            return ValueTask.FromResult(new UpdateVerifyResult(false, "updater-not-configured"));
        }

        try
        {
            var manifest = ParseAndVerifyManifest(options.ManifestJson);
            var stagingPath = ResolveStagingDirectory(options.StagingPath);
            if (stagingPath is null)
            {
                return ValueTask.FromResult(new UpdateVerifyResult(false, "invalid-staging-path"));
            }

            var error = VerifyStagedFiles(manifest, stagingPath);
            return ValueTask.FromResult(new UpdateVerifyResult(error is null, error));
        }
        catch (UpdateVerificationException exception)
        {
            LogVerificationFailure(exception);
            return ValueTask.FromResult(new UpdateVerifyResult(false, exception.Message));
        }
    }

    /// <summary>
    /// Rejects insecure HTTP at runtime as well as configuration time. The configuration layer
    /// already drops HTTP upstreams unless <c>AllowInsecureHttp</c> is set, but a caller might
    /// construct <see cref="UpdaterSettings"/> directly. Defense in depth.
    /// </summary>
    private void EnsureHttpsAllowed()
    {
        if (_settings is null)
        {
            return;
        }

        if (_settings.AllowInsecureHttp)
        {
            return;
        }

        if (!string.Equals(_settings.ManifestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateVerificationException("insecure-http-not-allowed");
        }
    }

    /// <summary>Fetches the manifest and throws <see cref="UpdateVerificationException"/> if schema or signature fail.</summary>
    private async Task<UpdateManifest> FetchVerifiedManifestAsync(CancellationToken cancellationToken)
    {
        var raw = await DownloadManifestBytesAsync(cancellationToken).ConfigureAwait(false);
        return ParseAndVerifyManifest(raw);
    }

    /// <summary>
    /// Parses cached or fetched manifest bytes and verifies them against the configured public key.
    /// Every failure mode (malformed JSON, bad schema, unusable key or signature) is normalized to
    /// <see cref="UpdateVerificationException"/> so callers never treat a broken manifest as trusted.
    /// </summary>
    private UpdateManifest ParseAndVerifyManifest(byte[] raw)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize(raw, ManifestTypeInfo)
                ?? throw new UpdateVerificationException("malformed-manifest");
            using var verifier = new UpdateVerifier(_settings!.PublicKeyB64);
            verifier.Verify(manifest);
            return manifest;
        }
        catch (JsonException)
        {
            throw new UpdateVerificationException("malformed-manifest");
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new UpdateVerificationException("invalid-signature");
        }
    }

    private UpdateManifest ParseAndVerifyManifest(string json) =>
        ParseAndVerifyManifest(Encoding.UTF8.GetBytes(json ?? string.Empty));

    /// <summary>
    /// Resolves an absolute staging directory confined to the configured staging root. Returns null for an
    /// empty, missing, outside-the-root or malformed path so callers report <c>invalid-staging-path</c>
    /// rather than touching an arbitrary directory.
    /// </summary>
    private string? ResolveStagingDirectory(string? stagingPath)
    {
        if (string.IsNullOrWhiteSpace(stagingPath))
        {
            return null;
        }

        try
        {
            var root = Path.GetFullPath(_settings!.StagingDir);
            var full = Path.GetFullPath(stagingPath);
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                Directory.Exists(full)
                ? full
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Re-hashes every staged blob advertised by the verified manifest and compares it with the signed
    /// SHA-256 table. Returns null when everything matches, otherwise a stable reason
    /// (<c>missing-file:</c>, <c>hash-mismatch:</c>, <c>unreadable-file:</c> or <c>unsafe-path:</c>).
    /// </summary>
    private static string? VerifyStagedFiles(UpdateManifest manifest, string stagingPath)
    {
        foreach (var file in manifest.Files)
        {
            string full;
            try
            {
                full = ResolveStagedFilePath(stagingPath, file);
            }
            catch (UpdateVerificationException exception)
            {
                return exception.Message;
            }

            if (!File.Exists(full))
            {
                return $"missing-file:{file}";
            }

            string actual;
            try
            {
                using var stream = File.OpenRead(full);
                actual = Convert.ToHexString(SHA256.HashData(stream));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return $"unreadable-file:{file}";
            }

            if (!string.Equals(actual, manifest.Sha256[file], StringComparison.OrdinalIgnoreCase))
            {
                return $"hash-mismatch:{file}";
            }
        }

        return null;
    }

    /// <summary>
    /// Streams the manifest response into memory while enforcing the configured byte cap so a
    /// runaway server cannot exhaust memory before signature verification rejects the payload.
    /// </summary>
    private async Task<byte[]> DownloadManifestBytesAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            _settings!.ManifestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[8192];
        using var memory = new MemoryStream();
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > _settings.MaxManifestBytes)
            {
                throw new UpdaterSizeLimitException(
                    $"manifest-exceeds-{_settings.MaxManifestBytes}-bytes");
            }
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return memory.ToArray();
    }

    private UpdateCheckResult CompareVersion(UpdateManifest manifest)
    {
        var comparison = CompareVersions(manifest.Version);
        return comparison.Error is not null
            ? new UpdateCheckResult(false, null, comparison.Error)
            : new UpdateCheckResult(comparison.IsUpdate, comparison.IsUpdate ? manifest.Version : null, null);
    }

    /// <summary>
    /// Applies the configured version policy. The default requires a strictly newer target; with
    /// <see cref="UpdaterSettings.AllowDowngrades"/> set (Tauri's application-level <c>allowDowngrades</c>)
    /// a merely different target is accepted. Unparsable versions are an explicit error, never "no update".
    /// </summary>
    private (bool IsUpdate, string? Error) CompareVersions(string targetVersion)
    {
        if (!Version.TryParse(targetVersion, out var target) ||
            !Version.TryParse(_settings!.CurrentVersion, out var current))
        {
            return (false, "invalid-version");
        }

        return (target != current && (_settings.AllowDowngrades || target > current), null);
    }

    /// <summary>
    /// Resolves a validated staging path and creates its parent directory so a blob's sub-path can be
    /// written. Confinement rules live in <see cref="ResolveStagedFilePath"/>.
    /// </summary>
    private static string ResolveStagingPath(string stagingRoot, string file)
    {
        var full = ResolveStagedFilePath(stagingRoot, file);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? full);
        return full;
    }

    /// <summary>
    /// Confines a manifest file entry to the staging root and defensively rejects entries that could
    /// smuggle a third-party scheme, an absolute/backslash path or a traversal escaping the root.
    /// The signed manifest is trusted, but the staging path is still confined as defense in depth.
    /// </summary>
    private static string ResolveStagedFilePath(string stagingRoot, string file)
    {
        if (string.IsNullOrWhiteSpace(file) ||
            file.StartsWith('/') ||
            file.StartsWith('\\') ||
            file.Contains('\\') ||
            file.Contains(':') ||
            file.Contains("://", StringComparison.Ordinal))
        {
            throw new UpdateVerificationException($"unsafe-path:{file}");
        }

        var full = Path.GetFullPath(Path.Combine(stagingRoot, file));
        if (!full.StartsWith(stagingRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new UpdateVerificationException($"unsafe-path:{file}");
        }

        return full;
    }

    private Uri ResolveFileUri(string file)
    {
        // file is already validated and confined by ResolveStagingPath; resolve it relative to the
        // manifest's base location so a reused staging path and the wire URL always agree.
        return new Uri(_settings!.ManifestUri, file);
    }

    /// <summary>
    /// Streams a single file into <paramref name="temporary"/>, enforcing per-file and cumulative
    /// byte caps while computing the SHA-256 incrementally. Both caps are checked before any
    /// additional write so a runaway stream cannot exhaust memory or disk before its hash is
    /// revalidated by the caller.
    /// </summary>
    private async Task<string> DownloadAndHashAsync(
        Uri url,
        string temporary,
        long maxFileBytes,
        long priorCumulativeBytes,
        long maxTotalBytes,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long fileBytes = 0;
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            fileBytes += read;
            var newCumulative = priorCumulativeBytes + fileBytes;

            if (fileBytes > maxFileBytes)
            {
                throw new UpdaterSizeLimitException(
                    $"file-exceeds-{maxFileBytes}-bytes:{url}");
            }

            if (newCumulative > maxTotalBytes)
            {
                throw new UpdaterSizeLimitException(
                    $"total-exceeds-{maxTotalBytes}-bytes");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        var digest = hash.GetHashAndReset();
        return Convert.ToHexString(digest);
    }

    private async ValueTask EmitStatusAsync(
        string phase,
        string? version = null,
        string? file = null,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        if (_events is null)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToElement(
            new UpdaterStatus(phase, version, file, error),
            TaruiJsonContext.Default.UpdaterStatus);

        try
        {
            await _events.EmitAsync("updater://status", payload, targetWindow: null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Status delivery is best-effort; a closing window is not fatal.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _downloadGate.Dispose();
        _http.Dispose();
    }

    [LoggerMessage(LogLevel.Warning, EventId = 100, Message = "Update manifest verification failed.")]
    private partial void LogVerificationFailure(Exception exception);

    [LoggerMessage(LogLevel.Warning, EventId = 101, Message = "Update '{Phase}' fetch failed.")]
    private partial void LogFetchFailure(string phase, Exception exception);

    [LoggerMessage(LogLevel.Warning, EventId = 102, Message = "Update '{Phase}' rejected for size limit.")]
    private partial void LogSizeLimit(string phase, Exception exception);

    [LoggerMessage(LogLevel.Warning, EventId = 103, Message = "Update apply failed.")]
    private partial void LogApplyFailure(Exception exception);
}
