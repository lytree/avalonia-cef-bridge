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
                Assert(File.ReadAllBytes(Path.Combine(staging, "app.tar.gz")).SequenceEqual(first),
                    "The staged blob must match the served bytes.");
                Assert(File.ReadAllBytes(Path.Combine(staging, "sub", "0.2.tar.gz")).SequenceEqual(second),
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
            staging ?? TempStaging());
        return new UpdaterService(http, settings, null, NullLogger<UpdaterService>.Instance);
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