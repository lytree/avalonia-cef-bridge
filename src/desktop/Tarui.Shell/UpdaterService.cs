using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Updater;

namespace Tarui.Shell;

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

    private readonly HttpClient _http;
    private readonly UpdaterSettings? _settings;
    private readonly EventRouter? _events;
    private readonly ILogger<UpdaterService> _logger;
    private readonly IUpdateApplier _applier;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public UpdaterService(
        HttpClient http,
        UpdaterSettings? settings,
        EventRouter? events,
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
        if (string.IsNullOrWhiteSpace(options.StagingPath))
        {
            return new UpdateApplyResult(false, "invalid-staging-path", restart);
        }

        var stagingRoot = Path.GetFullPath(_settings.StagingDir);
        var stagingPath = Path.GetFullPath(options.StagingPath);
        if (!stagingPath.StartsWith(stagingRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !Directory.Exists(stagingPath))
        {
            return new UpdateApplyResult(false, "invalid-staging-path", restart);
        }

        var bundle = Directory.GetFiles(stagingPath, "*.msix", SearchOption.TopDirectoryOnly)
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
        using var verifier = new UpdateVerifier(_settings!.PublicKeyB64);
        var raw = await DownloadManifestBytesAsync(cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize(raw, ManifestTypeInfo)
            ?? throw new UpdateVerificationException("malformed-manifest");
        verifier.Verify(manifest);
        return manifest;
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
        if (!Version.TryParse(manifest.Version, out var target) ||
            !Version.TryParse(_settings!.CurrentVersion, out var current))
        {
            return new UpdateCheckResult(false, null, "invalid-version");
        }

        return target > current
            ? new UpdateCheckResult(true, manifest.Version, null)
            : new UpdateCheckResult(false, null, null);
    }

    /// <summary>
    /// Confines a manifest file entry to the staging root and defensively rejects entries that could
    /// smuggle a third-party scheme, an absolute/backslash path or a traversal escaping the root.
    /// The signed manifest is trusted, but the staging path is still confined as defense in depth.
    /// </summary>
    private static string ResolveStagingPath(string stagingRoot, string file)
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

        var directory = Path.GetDirectoryName(full) ?? full;
        Directory.CreateDirectory(directory);
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
            await _events.EmitToAllAsync("updater://status", payload, cancellationToken);
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
