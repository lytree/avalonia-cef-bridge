using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tarui.WebView.Abstractions;
using Tarui.WebView.Avalonia;
using Tarui.WebView.CefGlueNext;

namespace Tarui.WebView.Tests;

internal static class Program
{
    public static int Main()
    {
        using var fixture = TestFixture.Create();

        TestHttpOptions();
        TestHttpModeServesCustomScheme(fixture);
        TestSchemeOptions(fixture);
        TestFromConfigurationHttp();
        TestFromConfigurationHttpWithSchemeRoot(fixture);
        TestFromConfigurationScheme(fixture);
        TestFromConfigurationEnvironmentKeyFallback(fixture);
        TestFromConfigurationInvalidMode();
        TestFromConfigurationSchemeSettings(fixture);
        TestAddCefGlueWebView();
        TestTaruiAppOrigin();
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
        Assert(
            http.AllowedSchemes.SequenceEqual(["http", "https"]),
            "HTTP mode without a content root should accept only http and https.");
        Assert(http.SchemeOrigin is null, "HTTP mode without a content root should not expose a scheme origin.");

        var https = CefGlueNextWebAppOptions.CreateHttp(
            new Uri("https://example.test/app"));
        AssertEqual("https", https.StartUri.Scheme, "HTTPS should be accepted.");
        Assert(
            https.AllowedSchemes.Contains("http") && https.AllowedSchemes.Contains("https"),
            "HTTP and HTTPS should both be accepted schemes.");

        AssertThrows<InvalidOperationException>(
            () => CefGlueNextWebAppOptions.CreateHttp(new Uri("file:///tmp/app")),
            "Non-HTTP schemes must be rejected by CreateHttp.");
        AssertThrows<InvalidOperationException>(
            () => CefGlueNextWebAppOptions.CreateHttp(new Uri("tarui://localhost/index.html")),
            "Custom schemes must be rejected as the HTTP start URI.");
    }

    private static void TestHttpModeServesCustomScheme(TestFixture fixture)
    {
        var options = CefGlueNextWebAppOptions.CreateHttp(
            new Uri("http://127.0.0.1:5173/app"),
            fixture.Root,
            schemeName: "app",
            domainName: "local");

        AssertEqual(TaruiWebResourceMode.Http, options.Mode, "HTTP mode should stay selected with a scheme root.");
        AssertEqual(
            "http://127.0.0.1:5173/app",
            options.StartUri.ToString(),
            "The HTTP start URI should stay the primary origin.");
        AssertEqual(Path.GetFullPath(fixture.Root), options.ContentRoot, "The scheme root should be served in HTTP mode.");
        AssertEqual("app", options.SchemeName, "The auxiliary scheme name should be preserved.");
        AssertEqual("local", options.DomainName, "The auxiliary scheme domain should be preserved.");
        AssertEqual(
            "app://local/",
            options.SchemeOrigin?.ToString(),
            "The auxiliary scheme origin should be portless.");
        Assert(
            options.AllowedSchemes.SequenceEqual(["http", "https", "app"]),
            "HTTP mode with a scheme root should accept http, https and the custom scheme together.");

        var emptyRoot = Path.Combine(Path.GetTempPath(), "tarui-webview-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRoot);
        try
        {
            AssertThrows<DirectoryNotFoundException>(
                () => CefGlueNextWebAppOptions.CreateHttp(new Uri("http://127.0.0.1:5173/"), emptyRoot),
                "A configured HTTP-mode root without index.html must be rejected.");
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
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
        Assert(
            options.AllowedSchemes.SequenceEqual(["tarui", "http", "https"]),
            "Scheme mode should accept the custom scheme plus http and https.");
        AssertEqual(
            "tarui://localhost/",
            options.SchemeOrigin?.ToString(),
            "Scheme mode should expose the portless custom scheme origin.");
    }

    private static void TestFromConfigurationHttp()
    {
        var configuration = CreateConfiguration(
            ("Tarui:Web:Mode", "http"),
            ("Tarui:Web:Url", "http://127.0.0.1:9999"));
        var options = CefGlueNextWebAppOptions.FromConfiguration(configuration);

        AssertEqual(TaruiWebResourceMode.Http, options.Mode, "Configured HTTP mode should be selected.");
        AssertEqual(
            new Uri("http://127.0.0.1:9999"),
            options.StartUri,
            "Configured URL should become the HTTP start URI.");
        Assert(
            options.SchemeOrigin is null,
            "HTTP mode without a configured root should not serve a custom scheme.");
    }

    private static void TestFromConfigurationHttpWithSchemeRoot(TestFixture fixture)
    {
        var configuration = CreateConfiguration(
            ("Tarui:Web:Mode", "http"),
            ("Tarui:Web:Url", "http://127.0.0.1:9999"),
            ("Tarui:Web:Root", fixture.Root),
            ("Tarui:Web:Scheme", "app"),
            ("Tarui:Web:Host", "local"));
        var options = CefGlueNextWebAppOptions.FromConfiguration(configuration);

        AssertEqual(TaruiWebResourceMode.Http, options.Mode, "Configured HTTP mode should stay selected.");
        AssertEqual(
            Path.GetFullPath(fixture.Root),
            options.ContentRoot,
            "A configured root should be served alongside the HTTP origin.");
        AssertEqual(
            "app://local/",
            options.SchemeOrigin?.ToString(),
            "The configured scheme and host should form the portless auxiliary origin.");
        Assert(
            options.AllowedSchemes.SequenceEqual(["http", "https", "app"]),
            "HTTP mode with a configured root should accept both families of schemes.");
    }

    private static void TestFromConfigurationScheme(TestFixture fixture)
    {
        var configuration = CreateConfiguration(
            ("Tarui:Web:Mode", "scheme"),
            ("Tarui:Web:Root", fixture.Root),
            ("Tarui:Web:Scheme", "app"),
            ("Tarui:Web:Host", "local"));
        var options = CefGlueNextWebAppOptions.FromConfiguration(configuration);

        AssertEqual(TaruiWebResourceMode.Scheme, options.Mode, "Configured scheme mode should be selected.");
        AssertEqual(
            "app://local/index.html",
            options.StartUri.ToString(),
            "Configured scheme and host should form the start URI.");
        AssertEqual(
            Path.GetFullPath(fixture.Root),
            options.ContentRoot,
            "Configured root should be normalized as the content root.");
    }

    private static void TestFromConfigurationEnvironmentKeyFallback(TestFixture fixture)
    {
        var configuration = CreateConfiguration(
            ("TARUI_WEB_MODE", "scheme"),
            ("TARUI_WEB_ROOT", fixture.Root));
        var options = CefGlueNextWebAppOptions.FromConfiguration(configuration);

        AssertEqual(
            TaruiWebResourceMode.Scheme,
            options.Mode,
            "Flat environment-style keys should be honored when hierarchical keys are absent.");
        AssertEqual(
            Path.GetFullPath(fixture.Root),
            options.ContentRoot,
            "Flat TARUI_WEB_ROOT key should configure the content root.");
    }

    private static void TestFromConfigurationInvalidMode()
    {
        var configuration = CreateConfiguration(("Tarui:Web:Mode", "ftp"));

        AssertThrows<InvalidOperationException>(
            () => CefGlueNextWebAppOptions.FromConfiguration(configuration),
            exception => exception.Message.Contains("TARUI_WEB_MODE", StringComparison.Ordinal),
            "Invalid configured mode must be rejected with a TARUI_WEB_MODE message.");
    }

    private static void TestFromConfigurationSchemeSettings(TestFixture fixture)
    {
        var configuration = CreateConfiguration(
            ("Tarui:Web:Mode", "scheme"),
            ("Tarui:Web:Root", fixture.Root),
            ("Tarui:Web:SpaFallback", "False"),
            ("Tarui:Web:Csp", "default-src 'self'"),
            ("Tarui:Web:MaxAssetBytes", "2048"));
        var options = CefGlueNextWebAppOptions.FromConfiguration(configuration);

        AssertEqual(false, options.SpaFallback, "Configured SpaFallback should be parsed case-insensitively.");
        AssertEqual(
            "default-src 'self'",
            options.ContentSecurityPolicy,
            "Configured CSP should be preserved.");
        AssertEqual(2048L, options.MaxAssetBytes, "Configured asset limit should be parsed.");

        AssertThrows<InvalidOperationException>(
            () => CefGlueNextWebAppOptions.FromConfiguration(CreateConfiguration(
                ("Tarui:Web:Mode", "scheme"),
                ("Tarui:Web:Root", fixture.Root),
                ("Tarui:Web:SpaFallback", "yes"))),
            "Invalid SpaFallback values must be rejected.");
        AssertThrows<InvalidOperationException>(
            () => CefGlueNextWebAppOptions.FromConfiguration(CreateConfiguration(
                ("Tarui:Web:Mode", "scheme"),
                ("Tarui:Web:Root", fixture.Root),
                ("Tarui:Web:MaxAssetBytes", "-1"))),
            "Non-positive MaxAssetBytes values must be rejected.");
    }

    private static void TestAddCefGlueWebView()
    {
        var configuration = CreateConfiguration(
            ("Tarui:Web:Mode", "http"),
            ("Tarui:Web:Url", "http://127.0.0.1:4321"));
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddCefGlueWebView();
        Assert(
            services.Any(static descriptor => descriptor.ServiceType == typeof(ITaruiAvaloniaWebViewFactory)),
            "AddCefGlueWebView should register the Avalonia WebView factory contract.");
        Assert(
            services.Any(static descriptor => descriptor.ServiceType == typeof(ITaruiWebViewFactory)),
            "AddCefGlueWebView should register the UI-neutral WebView factory contract.");
        using (var provider = services.BuildServiceProvider())
        {
            var options = provider.GetRequiredService<CefGlueNextWebAppOptions>();
            var origin = provider.GetRequiredService<TaruiAppOrigin>();

            AssertEqual(
                new Uri("http://127.0.0.1:4321"),
                options.StartUri,
                "AddCefGlueWebView should build options from IConfiguration.");
            AssertEqual(
                options.StartUri,
                origin.StartUri,
                "AddCefGlueWebView should register TaruiAppOrigin from the configured options.");
            Assert(
                origin.Schemes.SequenceEqual(options.AllowedSchemes),
                "TaruiAppOrigin should carry every scheme accepted by the options.");
            Assert(
                origin.SchemeOrigin == options.SchemeOrigin,
                "TaruiAppOrigin should carry the scheme origin from the options.");
        }

        var explicitOptions = CefGlueNextWebAppOptions.CreateHttp(
            new Uri("http://127.0.0.1:7777"));
        using (var provider = new ServiceCollection()
            .AddCefGlueWebView(explicitOptions)
            .BuildServiceProvider())
        {
            var options = provider.GetRequiredService<CefGlueNextWebAppOptions>();
            var origin = provider.GetRequiredService<TaruiAppOrigin>();

            Assert(
                ReferenceEquals(explicitOptions, options),
                "The explicit AddCefGlueWebView overload should register the provided options instance.");
            AssertEqual(
                explicitOptions.StartUri,
                origin.StartUri,
                "The explicit AddCefGlueWebView overload should derive TaruiAppOrigin from the provided instance.");
            Assert(
                origin.AllowsScheme("http") && origin.AllowsScheme("https"),
                "The explicit overload should preserve the multi-scheme acceptance.");
        }
    }

    private static void TestTaruiAppOrigin()
    {
        var uri = new Uri("tarui://localhost/index.html");

        AssertEqual(
            uri,
            new TaruiAppOrigin(uri).StartUri,
            "TaruiAppOrigin should expose the configured start URI.");

        var single = new TaruiAppOrigin(uri);
        Assert(
            single.Schemes.SequenceEqual(["tarui"]),
            "Without an explicit scheme list the start URI scheme is the only accepted scheme.");
        Assert(single.AllowsScheme("tarui"), "The start URI scheme must be accepted.");
        Assert(!single.AllowsScheme("http"), "Unlisted schemes must be rejected without an explicit list.");

        var multi = new TaruiAppOrigin(
            new Uri("http://127.0.0.1:5173/"),
            ["http", "https", "tarui"],
            new Uri("tarui://localhost/"));
        Assert(multi.AllowsScheme("tarui"), "A configured custom scheme must be accepted.");
        Assert(multi.AllowsScheme("TARUI"), "Scheme checks must be case-insensitive.");
        Assert(multi.AllowsScheme("https"), "HTTPS must be accepted alongside HTTP.");
        Assert(!multi.AllowsScheme("file"), "Unlisted schemes must be rejected.");
        AssertEqual(
            "tarui://localhost/",
            multi.SchemeOrigin?.ToString(),
            "The scheme origin should round-trip unchanged.");
    }

    private static void TestGetMimeAndQuery(TestFixture fixture)
    {
        var resolver = fixture.CreateResolver(spaFallback: false);
        using var asset = resolver.Resolve(
            "tarui://localhost/assets/app.js?cacheBust=42",
            "GET",
            allowSpaFallback: false);

        AssertEqual(200, asset.Status, "GET should return 200.");
        AssertEqual("application/javascript", asset.MimeType, "JavaScript MIME type should be selected.");
        AssertEqual("console.log('tarui');", Encoding.UTF8.GetString(ReadAllBytes(asset.Content)), "Query strings must not change the asset path.");
        AssertEqual(asset.ResponseLength, asset.ResponseLength, "GET response length should match the body.");
    }

    private static void TestHeadLength(TestFixture fixture)
    {
        var resolver = fixture.CreateResolver(spaFallback: false);
        using var get = resolver.Resolve("tarui://localhost/index.html", "GET", false);
        using var head = resolver.Resolve("tarui://localhost/index.html", "HEAD", false);

        AssertEqual(200, head.Status, "HEAD should return 200.");
        AssertEqual(0L, head.Content.Length, "HEAD must not return a body.");
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
            using var asset = resolver.Resolve(path, "GET", false);
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
        using var route = fallback.Resolve("tarui://localhost/settings/profile?tab=general", "GET", true);
        AssertEqual(200, route.Status, "SPA fallback should serve index.html for an allowed main-frame route.");
        AssertEqual("text/html", route.MimeType, "SPA fallback should retain index.html MIME type.");

        using var staticMissing = fallback.Resolve("tarui://localhost/assets/missing.js", "GET", true);
        AssertEqual(404, staticMissing.Status, "Missing static assets must not fall back to index.html.");

        using var disallowed = fallback.Resolve("tarui://localhost/settings/profile", "GET", false);
        AssertEqual(404, disallowed.Status, "SPA fallback requires allowSpaFallback=true.");
    }

    private static void TestAssetSizeLimit(TestFixture fixture)
    {
        var resolver = fixture.CreateResolver(spaFallback: false, maxAssetBytes: 4);
        using var asset = resolver.Resolve("tarui://localhost/assets/app.js", "GET", false);

        AssertEqual(413, asset.Status, "Assets over the configured size limit must return 413.");
    }

    private static void TestAssetCachePolicy(TestFixture fixture)
    {
        var resolver = fixture.CreateResolver(spaFallback: false);
        using var immutable = resolver.Resolve("tarui://localhost/assets/app.js", "GET", false);
        using var mutable = resolver.Resolve("tarui://localhost/index.html", "GET", false);

        AssertEqual(
            "public, max-age=31536000, immutable",
            immutable.CacheControl,
            "Assets under the assets directory should be immutable-cacheable.");
        AssertEqual("no-cache", mutable.CacheControl, "HTML entry points should not be cached as immutable assets.");
    }

    private static string Describe(string value) =>
        value.Replace("\0", "\\0", StringComparison.Ordinal).Replace("\u0001", "\\u0001", StringComparison.Ordinal);

    private static IConfigurationRoot CreateConfiguration(params (string Key, string? Value)[] settings)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

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

    private static void AssertThrows<TException>(Action action, Func<TException, bool> predicate, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            if (predicate(exception))
            {
                return;
            }

            throw new InvalidOperationException($"{message} Unexpected message: {exception.Message}");
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

    private static byte[] ReadAllBytes(Stream stream)
    {
        // The streaming resolver hands back FileStream instances that keep the source file locked; the
        // caller has no use for the stream after reading so we dispose it here so the test fixture can
        // delete its scratch directory on tear-down.
        if (stream is null) return [];
        using var memory = new MemoryStream();
        using (stream)
        {
            if (stream.CanSeek) stream.Position = 0;
            stream.CopyTo(memory);
        }
        return memory.ToArray();
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
