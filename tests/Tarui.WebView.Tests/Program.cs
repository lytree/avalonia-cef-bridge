using System.Text;
using Tarui.WebView.CefGlueNext;

namespace Tarui.WebView.Tests;

internal static class Program
{
    public static int Main()
    {
        using var fixture = TestFixture.Create();

        TestHttpOptions();
        TestSchemeOptions(fixture);
        TestGetMimeAndQuery(fixture);
        TestHeadLength(fixture);
        TestMethodAndAuthorityRejection(fixture);
        TestMissingAssetAndSpaFallback(fixture);
        TestAssetSizeLimit(fixture);
        TestAssetCachePolicy(fixture);
        TestPathRejection(fixture);

        Console.WriteLine("Tarui.WebView self-tests passed.");
        return 0;
    }

    private static void TestHttpOptions()
    {
        var http = CefGlueNextWebAppOptions.CreateHttp(
            new Uri("http://127.0.0.1:5173/app"));
        AssertEqual(TaruiWebResourceMode.Http, http.Mode, "HTTP mode should be selected.");
        AssertEqual("http://127.0.0.1:5173/app", http.StartUri.ToString(), "HTTP start URI should be preserved.");

        var https = CefGlueNextWebAppOptions.CreateHttp(
            new Uri("https://example.test/app"));
        AssertEqual("https", https.StartUri.Scheme, "HTTPS should be accepted.");

        AssertThrows<InvalidOperationException>(
            () => CefGlueNextWebAppOptions.CreateHttp(new Uri("file:///tmp/app")),
            "Non-HTTP schemes must be rejected by CreateHttp.");
        AssertThrows<InvalidOperationException>(
            () => CefGlueNextWebAppOptions.CreateHttp(new Uri("tarui://localhost/index.html")),
            "Custom schemes must be rejected by CreateHttp.");
    }

    private static void TestSchemeOptions(TestFixture fixture)
    {
        var options = CefGlueNextWebAppOptions.CreateScheme(
            fixture.Root,
            schemeName: "tarui",
            domainName: "localhost",
            spaFallback: true,
            maxAssetBytes: 1024);

        AssertEqual(TaruiWebResourceMode.Scheme, options.Mode, "Scheme mode should be selected.");
        AssertEqual(Path.GetFullPath(fixture.Root), options.ContentRoot, "Scheme root should be normalized.");
        AssertEqual("tarui", options.SchemeName, "Scheme name should be preserved.");
        AssertEqual("localhost", options.DomainName, "Scheme domain should be preserved.");
        AssertEqual("tarui://localhost/index.html", options.StartUri.ToString(), "Scheme start URI should target index.html.");
        AssertEqual(1024L, options.MaxAssetBytes, "Configured asset limit should be preserved.");
    }

    private static void TestGetMimeAndQuery(TestFixture fixture)
    {
        var resolver = fixture.CreateResolver(spaFallback: false);
        var asset = resolver.Resolve(
            "tarui://localhost/assets/app.js?cacheBust=42",
            "GET",
            allowSpaFallback: false);

        AssertEqual(200, asset.Status, "GET should return 200.");
        AssertEqual("application/javascript", asset.MimeType, "JavaScript MIME type should be selected.");
        AssertEqual("console.log('tarui');", Encoding.UTF8.GetString(asset.Content), "Query strings must not change the asset path.");
        AssertEqual(asset.Content.LongLength, asset.ResponseLength, "GET response length should match the body.");
    }

    private static void TestHeadLength(TestFixture fixture)
    {
        var resolver = fixture.CreateResolver(spaFallback: false);
        var get = resolver.Resolve("tarui://localhost/index.html", "GET", false);
        var head = resolver.Resolve("tarui://localhost/index.html", "HEAD", false);

        AssertEqual(200, head.Status, "HEAD should return 200.");
        AssertEqual(0L, head.Content.LongLength, "HEAD must not return a body.");
        AssertEqual(get.ResponseLength, head.ResponseLength, "HEAD length must equal the corresponding GET length.");
    }

    private static void TestMethodAndAuthorityRejection(TestFixture fixture)
    {
        var resolver = fixture.CreateResolver(spaFallback: false);

        AssertEqual(405, resolver.Resolve("tarui://localhost/index.html", "POST", false).Status, "POST must return 405.");
        AssertEqual(404, resolver.Resolve("tarui://localhost:123/index.html", "GET", false).Status, "Non-default ports must return 404.");
        AssertEqual(404, resolver.Resolve("tarui://user@localhost/index.html", "GET", false).Status, "Userinfo must return 404.");
        AssertEqual(404, resolver.Resolve("http://localhost/index.html", "GET", false).Status, "Unexpected schemes must return 404.");
    }

    private static void TestPathRejection(TestFixture fixture)
    {
        var resolver = fixture.CreateResolver(spaFallback: false);
        var rejectedPaths = new[]
        {
            "tarui://localhost/%2e%2e/index.html",
            "tarui://localhost/%5csecret.txt",
            "tarui://localhost/%3Asecret.txt",
            "tarui://localhost/bad\0name.txt",
            "tarui://localhost/bad\u0001name.txt"
        };

        foreach (var path in rejectedPaths)
        {
            var asset = resolver.Resolve(path, "GET", false);
            Assert(asset.Status != 200, $"Unsafe path should be rejected: {Describe(path)}");
        }
    }

    private static void TestMissingAssetAndSpaFallback(TestFixture fixture)
    {
        var noFallback = fixture.CreateResolver(spaFallback: false);
        AssertEqual(
            404,
            noFallback.Resolve("tarui://localhost/settings/profile", "GET", true).Status,
            "Missing extensionless assets must be 404 when SPA fallback is disabled.");

        var fallback = fixture.CreateResolver(spaFallback: true);
        var route = fallback.Resolve("tarui://localhost/settings/profile?tab=general", "GET", true);
        AssertEqual(200, route.Status, "SPA fallback should serve index.html for an allowed main-frame route.");
        AssertEqual("text/html", route.MimeType, "SPA fallback should retain index.html MIME type.");

        var staticMissing = fallback.Resolve("tarui://localhost/assets/missing.js", "GET", true);
        AssertEqual(404, staticMissing.Status, "Missing static assets must not fall back to index.html.");

        var disallowed = fallback.Resolve("tarui://localhost/settings/profile", "GET", false);
        AssertEqual(404, disallowed.Status, "SPA fallback requires allowSpaFallback=true.");
    }

    private static void TestAssetSizeLimit(TestFixture fixture)
    {
        var resolver = fixture.CreateResolver(spaFallback: false, maxAssetBytes: 4);
        var asset = resolver.Resolve("tarui://localhost/assets/app.js", "GET", false);

        AssertEqual(413, asset.Status, "Assets over the configured size limit must return 413.");
    }

    private static void TestAssetCachePolicy(TestFixture fixture)
    {
        var resolver = fixture.CreateResolver(spaFallback: false);
        var immutable = resolver.Resolve("tarui://localhost/assets/app.js", "GET", false);
        var mutable = resolver.Resolve("tarui://localhost/index.html", "GET", false);

        AssertEqual(
            "public, max-age=31536000, immutable",
            immutable.CacheControl,
            "Assets under the assets directory should be immutable-cacheable.");
        AssertEqual("no-cache", mutable.CacheControl, "HTML entry points should not be cached as immutable assets.");
    }

    private static string Describe(string value) =>
        value.Replace("\0", "\\0", StringComparison.Ordinal).Replace("\u0001", "\\u0001", StringComparison.Ordinal);

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; Actual: {actual}.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestFixture : IDisposable
    {
        private TestFixture(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TestFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "tarui-webview-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "assets"));
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>tarui</title>");
            File.WriteAllText(Path.Combine(root, "assets", "app.js"), "console.log('tarui');");
            File.WriteAllText(Path.Combine(root, "assets", "large.bin"), "0123456789");
            return new TestFixture(root);
        }

        public LocalWebAssetResolver CreateResolver(bool spaFallback, long maxAssetBytes = 1024 * 1024)
        {
            return new LocalWebAssetResolver(
                Root,
                CefGlueNextWebAppOptions.DefaultSchemeName,
                CefGlueNextWebAppOptions.DefaultDomainName,
                spaFallback,
                maxAssetBytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
