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
/// Drives the read-only updater flow. <c>check</c> fetches the signed manifest, verifies its ECDSA
/// signature and schema, and reports whether a strictly newer version exists. <c>download</c> repeats
/// that verification, then fetches each advertised file to the isolated staging directory after
/// revalidating its SHA-256. The running installation is never touched; staged paths are confined to
/// the staging root and file entries are validated so a tampered manifest cannot escape it.
/// Verification or hash failures surface as a Web-facing error, never as "no update".
/// Progress/completion is reported through the reserved <c>updater://status</c> event.
/// </summary>
public sealed partial class UpdaterService : IUpdaterService, IDisposable
{
    private static readonly JsonTypeInfo<UpdateManifest> ManifestTypeInfo = TaruiJsonContext.Default.UpdateManifest;

    private readonly HttpClient _http;
    private readonly UpdaterSettings? _settings;
    private readonly EventRouter? _events;
    private readonly ILogger<UpdaterService> _logger;

    public UpdaterService(
        HttpClient http,
        UpdaterSettings? settings,
        EventRouter? events,
        ILogger<UpdaterService> logger)
    {
        _http = http;
        _settings = settings;
        _events = events;
        _logger = logger;
    }

    public async ValueTask<UpdateCheckResult> CheckAsync(EmptyArgs options, CancellationToken cancellationToken)
    {
        if (_settings is null)
        {
            return new UpdateCheckResult(false, null, "updater-not-configured");
        }

        try
        {
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

        try
        {
            var manifest = await FetchVerifiedManifestAsync(cancellationToken);
            await EmitStatusAsync("download-start", manifest.Version, cancellationToken: cancellationToken);

            var stagingRoot = Path.GetFullPath(_settings.StagingDir);
            Directory.CreateDirectory(stagingRoot);
            ClearStaging(stagingRoot);

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
            return new UpdateDownloadResult(true, null);
        }
        catch (UpdateVerificationException exception)
        {
            LogVerificationFailure(exception);

            await EmitStatusAsync("download-failed", error: exception.Message, cancellationToken: cancellationToken);
            return new UpdateDownloadResult(false, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            LogFetchFailure("download", exception);

            await EmitStatusAsync("download-failed", error: "download-fetch-failed", cancellationToken: cancellationToken);
            return new UpdateDownloadResult(false, "download-fetch-failed");
        }
    }

    /// <summary>Fetches the manifest and throws <see cref="UpdateVerificationException"/> if schema or signature fail.</summary>
    private async Task<UpdateManifest> FetchVerifiedManifestAsync(CancellationToken cancellationToken)
    {
        using var verifier = new UpdateVerifier(_settings!.PublicKeyB64);
        var raw = await _http.GetByteArrayAsync(_settings.ManifestUri, cancellationToken);
        var manifest = JsonSerializer.Deserialize(raw, ManifestTypeInfo)
            ?? throw new UpdateVerificationException("malformed-manifest");
        verifier.Verify(manifest);
        return manifest;
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

    private async Task<string> DownloadAndHashAsync(Uri url, string temporary, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
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

    private static void ClearStaging(string stagingRoot)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(stagingRoot))
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            catch (IOException)
            {
                // Best-effort clean of a previous run; a lingering entry is harmless.
            }
            catch (UnauthorizedAccessException)
            {
            }
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

    public void Dispose() => _http.Dispose();

    [LoggerMessage(LogLevel.Warning, EventId = 100, Message = "Update manifest verification failed.")]
    private partial void LogVerificationFailure(Exception exception);

    [LoggerMessage(LogLevel.Warning, EventId = 101, Message = "Update '{Phase}' fetch failed.")]
    private partial void LogFetchFailure(string phase, Exception exception);
}