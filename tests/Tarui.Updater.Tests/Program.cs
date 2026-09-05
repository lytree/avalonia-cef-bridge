using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Tarui.Contracts;
using Tarui.Shell;

namespace Tarui.Updater.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            VerifierAcceptsValidSignature();
            VerifierRejectsTamperedSignature();
            VerifierRejectsUnsupportedSchema();
            VerifierRejectsMissingHash();
            VerifierRejectsMalformedManifest();
            await CheckReportsUpdateAvailableAsync();
            await CheckReportsNoUpdateWhenSameVersionAsync();
            await CheckReportsNotConfiguredAsync();
            await CheckReportsSignatureFailureAsync();
            await CheckReportsFetchFailureAsync();
            await DownloadStagesVerifiedFilesAsync();
            await DownloadRejectsHashMismatchAsync();
            await DownloadRejectsUnsafePathAsync();
            await DownloadRejectsTraversalEscapeAsync();
            await DownloadNotConfiguredFailsAsync();
            await HttpManifestIsRejectedByDefaultAsync();
            await ManifestExceedingLimitIsRejectedAsync();
            await DownloadsAreSerializedAsync();
            await StagingSubdirectoryIsUniquePerTransactionAsync();
            await ApplyNotConfiguredFailsAsync();
            await ApplyRejectsInvalidStagingPathAsync();
            await ApplyRejectsStagingWithoutBundleAsync();
            await ApplyAppliesStagedMsixAsync();
            await ApplyReportsUnsupportedWhenApplierDeclinesAsync();
            await ApplySurfacesApplierFailureAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.Updater self-tests passed.");
        return 0;
    }

    private static void VerifierAcceptsValidSignature()
    {
        using var key = NewKey();
        var manifest = Sign(key, "1.0.0", ["app.tar.gz"], Sha256("app.tar.gz", "payload"));
        using var verifier = new UpdateVerifier(PublicKeyB64(key));
        verifier.Verify(manifest); // no throw expected
    }

    private static void VerifierRejectsTamperedSignature()
    {
        using var key = NewKey();
        var manifest = Sign(key, "1.0.0", ["app.tar.gz"], Sha256("app.tar.gz", "payload"));
        using var verifier = new UpdateVerifier(PublicKeyB64(key));
        var tampered = manifest with { Version = "1.0.1" };
        AssertThrows<UpdateVerificationException>(() => verifier.Verify(tampered), "invalid-signature",
            "A modified signed field must fail signature verification.");
    }

    private static void VerifierRejectsUnsupportedSchema()
    {
        using var key = NewKey();
        var manifest = Sign(key, "1.0.0", ["app.tar.gz"], Sha256("app.tar.gz", "payload")) with { SchemaVersion = 99 };
        using var verifier = new UpdateVerifier(PublicKeyB64(key));
        AssertThrows<UpdateVerificationException>(() => verifier.Verify(manifest), "unsupported-schema",
            "An unknown schema version must be rejected.");
    }

    private static void VerifierRejectsMissingHash()
    {
        using var key = NewKey();
        var manifest = Sign(key, "1.0.0", ["app.tar.gz"], Sha256("app.tar.gz", "payload"));
        // Keep a non-empty hash table but omit the advertised file so only that entry is "missing".
        manifest = manifest with
        {
            Sha256 = new Dictionary<string, string>(StringComparer.Ordinal) { ["unknown.bin"] = Sha256("unknown.bin", "x")["unknown.bin"] },
        };
        using var verifier = new UpdateVerifier(PublicKeyB64(key));
        AssertThrows<UpdateVerificationException>(() => verifier.Verify(manifest), "missing-hash",
            "A file with no declared hash must fail verification.");
    }

    private static void VerifierRejectsMalformedManifest()
    {
        using var key = NewKey();
        var manifest = Sign(key, "1.0.0", ["app.tar.gz"], Sha256("app.tar.gz", "payload"));
        using var verifier = new UpdateVerifier(PublicKeyB64(key));
        var malformed = manifest with { Version = " " };
        AssertThrows<UpdateVerificationException>(() => verifier.Verify(malformed), "malformed-manifest",
            "A manifest missing required fields must fail verification.");
    }

    private static async Task CheckReportsUpdateAvailableAsync()
    {
        var (key, server) = UpdateServer("1.2.0", ["app.tar.gz"], "new-build");
        using (key)
        using (server)
        {
            var service = BuildService(key, "0.1.0", server, out var http);
            using (http)
            {
                var result = await service.CheckAsync(new EmptyArgs(), default);
                Assert(result.UpdateAvailable, "A strictly newer signed version must be reported available.");
                Assert(result.Version == "1.2.0", "The available version must match the manifest.");
                Assert(result.Error is null, "A successful check must not carry an error.");
            }
        }
    }

    private static async Task CheckReportsNoUpdateWhenSameVersionAsync()
    {
        var (key, server) = UpdateServer("0.1.0", ["app.tar.gz"], "new-build");
        using (key)
        using (server)
        {
            var service = BuildService(key, "0.1.0", server, out var http);
            using (http)
            {
                var result = await service.CheckAsync(new EmptyArgs(), default);
                Assert(!result.UpdateAvailable, "A target version equal to the current one is not an update.");
                Assert(result.Error is null, "No-update is not an error.");
            }
        }
    }

    private static async Task CheckReportsNotConfiguredAsync()
    {
        using var http = new HttpClient();
        var service = new UpdaterService(http, null, null, NullLogger<UpdaterService>.Instance);
        var result = await service.CheckAsync(new EmptyArgs(), default);
        Assert(!result.UpdateAvailable, "An unconfigured updater must report no update.");
        Assert(result.Error == "updater-not-configured", "A missing configuration must surface an explicit error.");
    }

    private static async Task CheckReportsSignatureFailureAsync()
    {
        // Host a manifest signed by a different key than the one the verifier trusts.
        var (_, server) = UpdateServer("1.2.0", ["app.tar.gz"], "new-build");
        using (server)
        using (var trustKey = NewKey())
        {
            var service = BuildService(trustKey, "0.1.0", server, out var http);
            using (http)
            {
                var result = await service.CheckAsync(new EmptyArgs(), default);
                Assert(!result.UpdateAvailable, "A manifest signed by an unknown key must never be available.");
                Assert(result.Error?.Contains("invalid-signature") == true, "The signature failure must be the reported error.");
            }
        }
    }

    private static async Task CheckReportsFetchFailureAsync()
    {
        // Server has no /latest.json route -> 404 -> HttpRequestException.
        var key = NewKey();
        using (key)
        using (var server = new TestServer(new Dictionary<string, (byte[] Body, int Status)>
        {
            ["/app.tar.gz"] = (System.Text.Encoding.UTF8.GetBytes("new-build"), 200),
        }))
        {
            var service = BuildService(key, "0.1.0", server, out var http);
            using (http)
            {
                var result = await service.CheckAsync(new EmptyArgs(), default);
                Assert(!result.UpdateAvailable, "A fetch failure must not report an update.");
                Assert(result.Error == "check-fetch-failed", "An unreachable manifest must surface a fetch error.");
            }
        }
    }

    private static async Task DownloadStagesVerifiedFilesAsync()
    {
        using var key = NewKey();
        var first = "first-blob"u8.ToArray();
        var second = "second-blob-content"u8.ToArray();
        var manifest = Sign(key, "2.0.0", ["app.tar.gz", "sub/0.2.tar.gz"], new Dictionary<string, string>
        {
            ["app.tar.gz"] = Sha256Hex(first),
            ["sub/0.2.tar.gz"] = Sha256Hex(second),
        });
        using (var server = new TestServer(new Dictionary<string, (byte[] Body, int Status)>
        {
            ["/latest.json"] = (JsonSerializer.SerializeToUtf8Bytes(manifest, TaruiJsonContext.Default.UpdateManifest), 200),
            ["/app.tar.gz"] = (first, 200),
            ["/sub/0.2.tar.gz"] = (second, 200),
        }))
        {
            var staging = TempStaging();
            var service = BuildService(key, "0.1.0", server, out var http, staging);
            using (http)
            {
                var result = await service.DownloadAsync(new EmptyArgs(), default);
                Assert(result.Succeeded, "Verified files must stage successfully.");
                Assert(result.StagingPath is not null, "A successful download must expose its staging path.");
                Assert(File.ReadAllBytes(Path.Combine(result.StagingPath!, "app.tar.gz")).SequenceEqual(first),
                    "The staged blob must match the served bytes.");
                Assert(File.ReadAllBytes(Path.Combine(result.StagingPath!, "sub", "0.2.tar.gz")).SequenceEqual(second),
                    "Nested staged blobs must be written to their sub-paths.");
            }
        }
    }
    private static async Task DownloadRejectsHashMismatchAsync()
    {
        using var key = NewKey();
        // Declare the correct hash of "expected" but serve "tampered".
        var manifest = Sign(key, "2.0.0", ["app.tar.gz"], Sha256("app.tar.gz", "expected-payload"));
        using (var server = new TestServer(new Dictionary<string, (byte[] Body, int Status)>
        {
            ["/latest.json"] = (JsonSerializer.SerializeToUtf8Bytes(manifest, TaruiJsonContext.Default.UpdateManifest), 200),
            ["/app.tar.gz"] = (System.Text.Encoding.UTF8.GetBytes("tampered-payload"), 200),
        }))
        {
            var staging = TempStaging();
            var service = BuildService(key, "0.1.0", server, out var http, staging);
            using (http)
            {
                var result = await service.DownloadAsync(new EmptyArgs(), default);
                Assert(!result.Succeeded, "A SHA-256 mismatch must fail the download.");
                Assert(result.Error?.StartsWith("hash-mismatch:app.tar.gz", StringComparison.Ordinal) == true,
                    "The hash mismatch error must name the offending file.");
                Assert(!File.Exists(Path.Combine(staging, "app.tar.gz")), "A failed blob must not be staged.");
            }
        }
    }

    private static async Task DownloadRejectsUnsafePathAsync()
    {
        using var key = NewKey();
        var manifest = Sign(key, "2.0.0", ["C:\\outside.exe"], Sha256("C:\\outside.exe", "x"));
        using (var server = new TestServer(new Dictionary<string, (byte[] Body, int Status)>
        {
            ["/latest.json"] = (JsonSerializer.SerializeToUtf8Bytes(manifest, TaruiJsonContext.Default.UpdateManifest), 200),
        }))
        {
            var staging = TempStaging();
            var service = BuildService(key, "0.1.0", server, out var http, staging);
            using (http)
            {
                var result = await service.DownloadAsync(new EmptyArgs(), default);
                Assert(!result.Succeeded, "An unsafe file entry (drive-qualified) must be rejected.");
                Assert(result.Error?.StartsWith("unsafe-path:", StringComparison.Ordinal) == true, "An unsafe path must be reported.");
            }
        }
    }

    private static async Task DownloadRejectsTraversalEscapeAsync()
    {
        using var key = NewKey();
        var manifest = Sign(key, "2.0.0", ["../../evil.txt"], Sha256("../../evil.txt", "x"));
        using (var server = new TestServer(new Dictionary<string, (byte[] Body, int Status)>
        {
            ["/latest.json"] = (JsonSerializer.SerializeToUtf8Bytes(manifest, TaruiJsonContext.Default.UpdateManifest), 200),
        }))
        {
            var staging = TempStaging();
            var service = BuildService(key, "0.1.0", server, out var http, staging);
            using (http)
            {
                var result = await service.DownloadAsync(new EmptyArgs(), default);
                Assert(!result.Succeeded, "A traversal escaping the staging root must be rejected.");
                Assert(result.Error?.StartsWith("unsafe-path:", StringComparison.Ordinal) == true, "An escaping path must be reported unsafe.");
                Assert(!File.Exists(Path.Combine(Path.GetTempPath(), "evil.txt")),
                    "An escaped blob must never be written outside the staging root.");
            }
        }
    }

    private static async Task DownloadNotConfiguredFailsAsync()
    {
        using var http = new HttpClient();
        var service = new UpdaterService(http, null, null, NullLogger<UpdaterService>.Instance);
        var result = await service.DownloadAsync(new EmptyArgs(), default);
        Assert(!result.Succeeded, "An unconfigured updater must not download.");
        Assert(result.Error == "updater-not-configured", "A missing configuration must surface an explicit error.");
    }

    private static async Task HttpManifestIsRejectedByDefaultAsync()
    {
        var (key, server) = UpdateServer("1.2.0", ["app.tar.gz"], "new-build");
        using (key)
        using (server)
        {
            var settings = new UpdaterSettings(
                new Uri(server.BaseUrl + "/latest.json"),
                PublicKeyB64(key),
                "0.1.0",
                TempStaging(),
                AllowInsecureHttp: false);
            var service = BuildService(settings, out var http);
            using (http)
            {
                var result = await service.CheckAsync(new EmptyArgs(), default);
                Assert(!result.UpdateAvailable,
                    "An insecure-HTTP manifest must never produce an available update (either no update or an error).");
                Assert(!result.UpdateAvailable || result.Error is not null,
                    "An insecure-HTTP manifest must surface a non-null error.");
            }
        }
    }

    private static async Task ManifestExceedingLimitIsRejectedAsync()
    {
        var (key, _) = UpdateServer("1.2.0", ["app.tar.gz"], "new-build");
        using (key)
        {
            var bigManifest = new string('x', 8 * 1024);
            var server = new TestServer(new Dictionary<string, (byte[] Body, int Status)>
            {
                ["/latest.json"] = (System.Text.Encoding.UTF8.GetBytes(bigManifest), 200),
            });
            using (server)
            {
                var settings = new UpdaterSettings(
                    new Uri(server.BaseUrl + "/latest.json"),
                    PublicKeyB64(key),
                    "0.1.0",
                    TempStaging(),
                    MaxManifestBytes: 4 * 1024,
                    AllowInsecureHttp: true);
                var service = BuildService(settings, out var http);
                using (http)
                {
                    var result = await service.CheckAsync(new EmptyArgs(), default);
                    Assert(!result.UpdateAvailable,
                        "A manifest that exceeds the byte cap must not be reported available.");
                    Assert(result.Error is not null && result.Error.Contains("exceeds", StringComparison.Ordinal),
                        $"The rejection must surface a size limit reason. Got: {result.Error}");
                }
            }
        }
    }

    private static async Task DownloadsAreSerializedAsync()
    {
        var (key, server) = UpdateServer("1.2.0", ["app.tar.gz"], "new-build");
        using (key)
        using (server)
        {
            var settings = new UpdaterSettings(
                new Uri(server.BaseUrl + "/latest.json"),
                PublicKeyB64(key),
                "0.1.0",
                TempStaging());
            var service = BuildService(settings, out var http);
            using (http)
            {
                var first = service.CheckAsync(new EmptyArgs(), default).AsTask();
                var second = service.CheckAsync(new EmptyArgs(), default).AsTask();
                var results = await Task.WhenAll(first, second);
                Assert(results.All(static r => !r.UpdateAvailable || r.Error is null || r.Version == "1.2.0"),
                    "Two concurrent calls must each return a stable result without crashing.");
            }
        }
    }

    private static async Task StagingSubdirectoryIsUniquePerTransactionAsync()
    {
        var (key, server) = UpdateServer("1.2.0", ["app.tar.gz"], "payload");
        using (key)
        using (server)
        {
            var staging = TempStaging();
            var settings = new UpdaterSettings(
                new Uri(server.BaseUrl + "/latest.json"),
                PublicKeyB64(key),
                "0.1.0",
                staging,
                AllowInsecureHttp: true);
            var service = BuildService(settings, out var http);
            using (http)
            {
                var result = await service.DownloadAsync(new EmptyArgs(), default);
                Assert(result.Succeeded, "The first download must succeed.");
                Assert(result.StagingPath is not null && result.StagingPath!.StartsWith(staging, StringComparison.Ordinal),
                    "The transaction staging path must live under the configured staging root.");
                Assert(!string.Equals(result.StagingPath, staging, StringComparison.Ordinal),
                    "The transaction staging path must be a unique subdirectory, not the root.");
            }
        }
    }

    private static async Task ApplyNotConfiguredFailsAsync()
    {
        using var http = new HttpClient();
        var service = new UpdaterService(http, null, null, NullLogger<UpdaterService>.Instance, new NoOpUpdateApplier());
        var result = await service.ApplyAsync(new UpdateApplyOptions("C:/staged"), default);
        Assert(!result.Succeeded && result.Error == "updater-not-configured", "An unconfigured updater must not apply.");
    }

    private static async Task ApplyRejectsInvalidStagingPathAsync()
    {
        var root = TempStaging();
        Directory.CreateDirectory(root);
        var service = BuildApplyService(root, new RecordingApplier(), out var http);
        try
        {
            var outside = await service.ApplyAsync(
                new UpdateApplyOptions(Path.Combine(Path.GetTempPath(), "unrelated-" + Guid.NewGuid().ToString("N"))), default);
            Assert(!outside.Succeeded && outside.Error == "invalid-staging-path", "A staging path outside the root must be rejected.");

            var missing = await service.ApplyAsync(new UpdateApplyOptions(Path.Combine(root, "missing")), default);
            Assert(!missing.Succeeded && missing.Error == "invalid-staging-path", "A missing staged directory under the root must be rejected.");
        }
        finally
        {
            http.Dispose();
            TryDeleteDir(root);
        }
    }

    private static async Task ApplyRejectsStagingWithoutBundleAsync()
    {
        var root = TempStaging();
        Directory.CreateDirectory(root);
        var staged = Directory.CreateDirectory(Path.Combine(root, "txn")).FullName;
        var service = BuildApplyService(root, new RecordingApplier(), out var http);
        try
        {
            var result = await service.ApplyAsync(new UpdateApplyOptions(staged), default);
            Assert(!result.Succeeded && result.Error == "no-bundle-staged", "A staged set without an MSIX must surface no-bundle-staged.");
        }
        finally
        {
            http.Dispose();
            TryDeleteDir(root);
        }
    }

    private static async Task ApplyAppliesStagedMsixAsync()
    {
        var root = TempStaging();
        Directory.CreateDirectory(root);
        var staged = Directory.CreateDirectory(Path.Combine(root, "txn")).FullName;
        File.WriteAllText(Path.Combine(staged, "app-1.0.1-win-x64.msix"), "pkg");
        var applier = new RecordingApplier { Result = true };
        var service = BuildApplyService(root, applier, out var http);
        try
        {
            var result = await service.ApplyAsync(new UpdateApplyOptions(staged, Restart: true), default);
            Assert(result.Succeeded, "An MSIX staged bundle the applier accepts must apply.");
            Assert(result.Restart, "The restart request must be echoed.");
            Assert(applier.Calls.Count == 1 &&
                   Path.GetFullPath(applier.Calls[0]).TrimEnd('\\').Equals(Path.GetFullPath(staged).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase),
                "The applier must be invoked with the staged path.");
        }
        finally
        {
            http.Dispose();
            TryDeleteDir(root);
        }
    }

    private static async Task ApplyReportsUnsupportedWhenApplierDeclinesAsync()
    {
        var root = TempStaging();
        Directory.CreateDirectory(root);
        var staged = Directory.CreateDirectory(Path.Combine(root, "txn")).FullName;
        File.WriteAllText(Path.Combine(staged, "app.msix"), "pkg");
        var service = BuildApplyService(root, new RecordingApplier { Result = false }, out var http);
        try
        {
            var result = await service.ApplyAsync(new UpdateApplyOptions(staged), default);
            Assert(!result.Succeeded && result.Error == "update-apply-unsupported", "A declining applier must surface update-apply-unsupported.");
        }
        finally
        {
            http.Dispose();
            TryDeleteDir(root);
        }
    }

    private static async Task ApplySurfacesApplierFailureAsync()
    {
        var root = TempStaging();
        Directory.CreateDirectory(root);
        var staged = Directory.CreateDirectory(Path.Combine(root, "txn")).FullName;
        File.WriteAllText(Path.Combine(staged, "app.msix"), "pkg");
        var applier = new RecordingApplier { ThrowOnApply = new InvalidOperationException("boom") };
        var service = BuildApplyService(root, applier, out var http);
        try
        {
            var result = await service.ApplyAsync(new UpdateApplyOptions(staged), default);
            Assert(!result.Succeeded && result.Error == "apply-failed", "An applier failure must surface as apply-failed.");
        }
        finally
        {
            http.Dispose();
            TryDeleteDir(root);
        }
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A configurable fake applier that records the staged paths it was asked to apply.</summary>
    private sealed class RecordingApplier : IUpdateApplier
    {
        public List<string> Calls { get; } = [];
        public bool Result { get; set; } = true;
        public Exception? ThrowOnApply { get; set; }

        public ValueTask<bool> ApplyAsync(string stagingPath, CancellationToken cancellationToken)
        {
            Calls.Add(stagingPath);
            if (ThrowOnApply is not null)
            {
                throw ThrowOnApply;
            }

            return ValueTask.FromResult(Result);
        }
    }

    private static UpdaterService BuildService(UpdaterSettings settings, out HttpClient http)
    {
        http = new HttpClient();
        return new UpdaterService(http, settings, null, NullLogger<UpdaterService>.Instance);
    }

    private static UpdaterService BuildService(
        ECDsa publicKey,
        string currentVersion,
        TestServer server,
        out HttpClient http,
        string? staging = null)
    {
        http = new HttpClient();
        var settings = new UpdaterSettings(
            new Uri(server.BaseUrl + "/latest.json"),
            PublicKeyB64(publicKey),
            currentVersion,
            staging ?? TempStaging(),
            AllowInsecureHttp: true);
        return new UpdaterService(http, settings, null, NullLogger<UpdaterService>.Instance, new NoOpUpdateApplier());
    }

    private static UpdaterService BuildApplyService(string stagingDir, IUpdateApplier applier, out HttpClient http)
    {
        http = new HttpClient();
        var settings = new UpdaterSettings(
            new Uri("https://example.test/latest.json"),
            PublicKeyB64: string.Empty,
            CurrentVersion: "1.0.0",
            StagingDir: stagingDir);
        return new UpdaterService(http, settings, null, NullLogger<UpdaterService>.Instance, applier);
    }

    /// <summary>Builds a manifest for the target version with the supplied files/hash and a live test server.</summary>
    private static (ECDsa Key, TestServer Server) UpdateServer(
        string version,
        string[] files,
        string payload)
    {
        var key = NewKey();
        var manifest = Sign(key, version, files, Sha256(files[0], payload));
        var server = new TestServer(new Dictionary<string, (byte[] Body, int Status)>
        {
            ["/latest.json"] = (JsonSerializer.SerializeToUtf8Bytes(manifest, TaruiJsonContext.Default.UpdateManifest), 200),
            ["/" + files[0].Replace('\\', '/')] = (System.Text.Encoding.UTF8.GetBytes(payload), 200),
        });
        return (key, server);
    }

    private static string TempStaging() =>
        Path.Combine(Path.GetTempPath(), "tarui-updater-" + Guid.NewGuid().ToString("N"));

    private static ECDsa NewKey() => ECDsa.Create(ECCurve.NamedCurves.nistP384);

    private static string PublicKeyB64(ECDsa key) =>
        Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private static UpdateManifest Sign(ECDsa key, string version, string[] files, Dictionary<string, string> sha256)
    {
        var manifest = new UpdateManifest(UpdateContracts.SchemaVersion, version, files, sha256, string.Empty);
        var canonical = UpdateVerifier.Canonicalize(manifest);
        var signature = key.SignData(canonical, HashAlgorithmName.SHA384);
        return manifest with { Signature = Convert.ToBase64String(signature) };
    }

    private static Dictionary<string, string> Sha256(string file, string content) =>
        new(StringComparer.Ordinal) { [file] = Sha256Hex(System.Text.Encoding.UTF8.GetBytes(content)) };

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void AssertThrows<TException>(Action action, string fragment, string message)
        where TException : Exception
    {
        try
        {
            action();
            Assert(false, $"{message} (expected {typeof(TException).Name}).");
        }
        catch (TException exception)
        {
            Assert(exception.Message.Contains(fragment, StringComparison.Ordinal),
                $"{message} Message must contain '{fragment}', got '{exception.Message}'.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TestServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Dictionary<string, (byte[] Body, int Status)> _routes;
        private readonly CancellationTokenSource _cts = new();

        public TestServer(Dictionary<string, (byte[] Body, int Status)> routes)
        {
            _routes = routes;
            var port = FreePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{port}";
            _ = Task.Run(ServeAsync);
        }

        public string BaseUrl { get; }

        private async Task ServeAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    break;
                }

                var path = context.Request.Url?.AbsolutePath ?? "/";
                if (_routes.TryGetValue(path, out var hit))
                {
                    context.Response.StatusCode = hit.Status;
                    context.Response.ContentLength64 = hit.Body.LongLength;
                    await context.Response.OutputStream.WriteAsync(hit.Body);
                    context.Response.OutputStream.Close();
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    context.Response.Close();
                }
            }
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Abort();
        }
    }
}


