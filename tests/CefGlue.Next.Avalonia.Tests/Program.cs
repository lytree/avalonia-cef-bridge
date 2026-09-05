using System.Text;
using Avalonia;
using CefGlue.Next.Avalonia;

namespace CefGlue.Next.Avalonia.Tests;

internal static class Program
{
    public static int Main()
    {
        RuntimeOptionsExposeStableDefaults();
        RuntimeOptionsDefaultCacheUsesProcessTempDirectory();
        RuntimeOptionsPreserveExplicitConfiguration();
        RuntimeOptionsWithProxyAddsCommandLineFlag();
        RuntimeRejectsMissingSubprocessBeforeNativeStartup();
        RuntimeRejectsInvalidSchemeOptionsBeforeNativeStartup();
        RunSubProcessIgnoresNormalApplicationArguments();
        SchemeOptionsPreserveResourceAndSecurityConfiguration();
        ResourceProviderContractCarriesRequestAndResponse();
        EventArgumentsDefaultToSafeDecisions();
        EventArgumentsPreserveMappingFields();
        ProviderBoundaryReturnsResponseOnSuccess();
        ProviderBoundaryConvertsExceptionToInternalServerError();
        RuntimeOptionsFingerprintStableForEquivalentOptions();
        RuntimeOptionsFingerprintDistinguishesSchemes();
        RuntimeOptionsFingerprintDistinguishesSubprocessAndCache();
        Console.WriteLine("CefGlue.Next.Avalonia self-tests passed.");
        return 0;
    }

    private static void RuntimeOptionsExposeStableDefaults()
    {
        var options = new CefGlueNextAvaloniaRuntimeOptions();

        Assert(options.NoSandbox, "NoSandbox should default to true.");
        Assert(!options.WindowlessRenderingEnabled, "Windowless rendering should default to disabled.");
        AssertEqual(
            CefGlueNextAvaloniaLogSeverity.Warning,
            options.LogSeverity,
            "Runtime logging should default to warning severity.");
        Assert(
            options.CommandLineFlags.Any(static flag =>
                flag.Key == "do-not-de-elevate" && flag.Value.Length == 0),
            "Runtime options should include the do-not-de-elevate flag by default.");
        Assert(options.Schemes.Count == 0, "Runtime options should not register schemes by default.");
    }

    private static void RuntimeOptionsPreserveExplicitConfiguration()
    {
        var schemes = new[]
        {
            new CefGlueNextAvaloniaSchemeOptions
            {
                SchemeName = "app",
                DomainName = "local",
                ResourceProvider = new RecordingResourceProvider()
            }
        };
        var options = new CefGlueNextAvaloniaRuntimeOptions
        {
            RuntimeDirectory = "runtime",
            ResourcesDirectory = "resources",
            LocalesDirectory = "locales",
            CacheDirectory = "cache",
            BrowserSubprocessPath = "subprocess.exe",
            WindowlessRenderingEnabled = true,
            NoSandbox = false,
            LogFile = "cef.log",
            LogSeverity = CefGlueNextAvaloniaLogSeverity.Info,
            CommandLineFlags = [new KeyValuePair<string, string>("flag", "value")],
            Schemes = schemes
        };

        AssertEqual("runtime", options.RuntimeDirectory, "Runtime directory should be preserved.");
        AssertEqual("resources", options.ResourcesDirectory, "Resources directory should be preserved.");
        AssertEqual("locales", options.LocalesDirectory, "Locales directory should be preserved.");
        AssertEqual("cache", options.CacheDirectory, "Cache directory should be preserved.");
        AssertEqual("subprocess.exe", options.BrowserSubprocessPath, "Subprocess path should be preserved.");
        Assert(options.WindowlessRenderingEnabled, "Windowless rendering should preserve explicit configuration.");
        Assert(!options.NoSandbox, "NoSandbox should preserve explicit configuration.");
        AssertEqual(CefGlueNextAvaloniaLogSeverity.Info, options.LogSeverity, "Log severity should be preserved.");
        AssertEqual("value", options.CommandLineFlags[0].Value, "Command line flags should be preserved.");
        Assert(ReferenceEquals(schemes, options.Schemes), "Scheme options should preserve the supplied collection.");
    }

    private static void RuntimeOptionsDefaultCacheUsesProcessTempDirectory()
    {
        var options = new CefGlueNextAvaloniaRuntimeOptions();
        var cacheDirectory = options.ResolveCacheDirectory();
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        Assert(
            cacheDirectory.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "The default CEF cache must be created below the process temp directory.");
        Assert(
            !cacheDirectory.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase),
            "The default CEF cache must not be created below AppContext.BaseDirectory.");
    }

    private static void RuntimeOptionsWithProxyAddsCommandLineFlag()
    {
        var options = new CefGlueNextAvaloniaRuntimeOptions { ProxyServer = "http://127.0.0.1:8080" };
        var flags = options.WithNetworkFlags();
        Assert(
            flags.Any(static flag => flag.Key == "proxy-server" && flag.Value == "http://127.0.0.1:8080"),
            "The proxy must be added as a CEF proxy-server command-line flag.");

        var replaces = new CefGlueNextAvaloniaRuntimeOptions
        {
            ProxyServer = "http://127.0.0.1:9",
            CommandLineFlags = [new KeyValuePair<string, string>("proxy-server", "old")],
        };
        var merged = replaces.WithNetworkFlags();
        Assert(merged.Count(static flag => flag.Key == "proxy-server") == 1, "A configured proxy must replace an existing proxy-server flag.");
        Assert(
            merged.First(static flag => flag.Key == "proxy-server").Value == "http://127.0.0.1:9",
            "The replaced proxy value must win.");

        var noProxy = new CefGlueNextAvaloniaRuntimeOptions();
        Assert(ReferenceEquals(noProxy.WithNetworkFlags(), noProxy.CommandLineFlags), "Without a proxy the original flags must pass through unchanged.");
    }

    private static void RuntimeRejectsMissingSubprocessBeforeNativeStartup()
    {
        using var directory = new TemporaryDirectory();
        var missingSubprocess = Path.Combine(directory.Path, "missing-subprocess.exe");
        var options = new CefGlueNextAvaloniaRuntimeOptions
        {
            RuntimeDirectory = directory.Path,
            CacheDirectory = Path.Combine(directory.Path, "cache"),
            BrowserSubprocessPath = missingSubprocess
        };

        try
        {
            CefGlueNextAvaloniaRuntime.Initialize(options);
        }
        catch (FileNotFoundException exception)
        {
            AssertEqual(
                Path.GetFullPath(missingSubprocess),
                exception.FileName,
                "Missing subprocess errors should report the normalized path.");
            return;
        }

        throw new InvalidOperationException(
            "A missing browser subprocess must fail before attempting native CEF startup.");
    }

    private static void RuntimeRejectsInvalidSchemeOptionsBeforeNativeStartup()
    {
        var provider = new RecordingResourceProvider();

        AssertThrows<ArgumentException>(
            () => CefGlueNextAvaloniaRuntime.Initialize(new CefGlueNextAvaloniaRuntimeOptions
            {
                Schemes =
                [
                    new CefGlueNextAvaloniaSchemeOptions
                    {
                        SchemeName = "not a scheme",
                        DomainName = "localhost",
                        ResourceProvider = provider
                    }
                ]
            }),
            "An invalid scheme name must be rejected before native initialization.");

        AssertThrows<ArgumentException>(
            () => CefGlueNextAvaloniaRuntime.Initialize(new CefGlueNextAvaloniaRuntimeOptions
            {
                Schemes =
                [
                    new CefGlueNextAvaloniaSchemeOptions
                    {
                        SchemeName = "app",
                        DomainName = "localhost:8080",
                        ResourceProvider = provider
                    }
                ]
            }),
            "A scheme host containing a port must be rejected before native initialization.");

        AssertThrows<ArgumentException>(
            () => CefGlueNextAvaloniaRuntime.Initialize(new CefGlueNextAvaloniaRuntimeOptions
            {
                Schemes =
                [
                    new CefGlueNextAvaloniaSchemeOptions
                    {
                        SchemeName = "app",
                        DomainName = "localhost",
                        ResourceProvider = null!
                    }
                ]
            }),
            "A scheme without a resource provider must be rejected before native initialization.");

        AssertThrows<ArgumentException>(
            () => CefGlueNextAvaloniaRuntime.Initialize(new CefGlueNextAvaloniaRuntimeOptions
            {
                Schemes =
                [
                    new CefGlueNextAvaloniaSchemeOptions
                    {
                        SchemeName = "app",
                        DomainName = "localhost",
                        ResourceProvider = provider
                    },
                    new CefGlueNextAvaloniaSchemeOptions
                    {
                        SchemeName = "APP",
                        DomainName = "LOCALHOST",
                        ResourceProvider = provider
                    }
                ]
            }),
            "Duplicate scheme origins must be rejected before native initialization.");
    }

    private static void RunSubProcessIgnoresNormalApplicationArguments()
    {
        Assert(
            !CefGlueNextAvaloniaRuntime.RunSubProcess(["--app=test"], exitAfterRun: false),
            "Normal application arguments must not enter the CEF subprocess path.");
    }

    private static void SchemeOptionsPreserveResourceAndSecurityConfiguration()
    {
        var provider = new RecordingResourceProvider();
        var options = new CefGlueNextAvaloniaSchemeOptions
        {
            SchemeName = "tarui",
            DomainName = "localhost",
            ResourceProvider = provider,
            IsStandard = true,
            IsLocal = false,
            IsDisplayIsolated = true,
            IsSecure = true,
            IsCorsEnabled = false,
            IsCspBypassing = false,
            IsFetchEnabled = true
        };

        AssertEqual("tarui", options.SchemeName, "Scheme name should be preserved.");
        AssertEqual("localhost", options.DomainName, "Scheme domain should be preserved.");
        Assert(ReferenceEquals(provider, options.ResourceProvider), "Scheme should preserve its resource provider.");
        Assert(options.IsStandard && options.IsDisplayIsolated && options.IsSecure, "Secure scheme flags should be preserved.");
        Assert(!options.IsLocal && !options.IsCorsEnabled && !options.IsCspBypassing, "Restrictive scheme flags should be preserved.");
        Assert(options.IsFetchEnabled, "Fetch support should be preserved.");
    }

    private static void ResourceProviderContractCarriesRequestAndResponse()
    {
        var provider = new RecordingResourceProvider();
        var request = new CefGlueNextAvaloniaResourceRequest(
            "tarui://localhost/index.html?mode=test",
            "GET",
            IsMainFrame: true,
            IsMainFrameResource: true);
        var response = provider.Resolve(request);

        Assert(ReferenceEquals(request, provider.LastRequest), "Resource providers should receive the original request.");
        AssertEqual(200, response.Status, "Resource responses should preserve status.");
        AssertEqual("text/html", response.MimeType, "Resource responses should preserve MIME type.");
        AssertEqual("index", Encoding.UTF8.GetString(response.Content), "Resource responses should preserve content.");
        AssertEqual(response.Content.LongLength, response.ResponseLength, "Response length should describe the content.");
    }

    private static void EventArgumentsDefaultToSafeDecisions()
    {
        var navigation = new CefGlueNextAvaloniaNavigationRequestedEventArgs(
            new Uri("https://example.test/"),
            isMainFrame: true,
            userGesture: false,
            isRedirect: false);
        var download = new CefGlueNextAvaloniaDownloadRequestedEventArgs(
            new Uri("https://example.test/file.bin"),
            "file.bin");
        var drop = new CefGlueNextAvaloniaFileDropEventArgs([], null, new Point(1, 2));
        var external = new CefGlueNextAvaloniaExternalNavigationEventArgs(new Uri("https://example.test/"));

        AssertEqual(CefGlueNextAvaloniaNavigationDecision.Deny, navigation.Decision, "Navigation must default to deny.");
        AssertEqual(CefGlueNextAvaloniaDownloadDecision.Deny, download.Decision, "Downloads must default to deny.");
        Assert(!drop.Accepted, "File drops must default to rejected until authorized.");
        Assert(!external.Handled, "External navigation must default to unhandled.");
        Assert(download.ShowDialog, "Downloads should show a dialog by default when explicitly allowed.");
    }

    private static void EventArgumentsPreserveMappingFields()
    {
        var uri = new Uri("https://example.test/route");
        var navigation = new CefGlueNextAvaloniaNavigationRequestedEventArgs(uri, true, true, true);
        var download = new CefGlueNextAvaloniaDownloadRequestedEventArgs(uri, "report.pdf");
        var drop = new CefGlueNextAvaloniaFileDropEventArgs(["C:\\a.txt"], "text", new Point(4, 5));
        var regions = new[] { new CefGlueNextAvaloniaDraggableRegion(1, 2, 3, 4, true) };
        var regionsEvent = new CefGlueNextAvaloniaDragRegionsUpdatedEventArgs(regions);

        AssertEqual(uri, navigation.Uri, "Navigation URI should map unchanged.");
        Assert(navigation.IsMainFrame && navigation.UserGesture && navigation.IsRedirect, "Navigation flags should map unchanged.");
        AssertEqual(uri, download.Uri, "Download URI should map unchanged.");
        AssertEqual("report.pdf", download.SuggestedFileName, "Suggested file name should map unchanged.");
        AssertEqual("text", drop.Text, "Drop text should map unchanged.");
        AssertEqual(new Point(4, 5), drop.Position, "Drop coordinates should map unchanged.");
        Assert(regionsEvent.Regions.Count == 1 && regionsEvent.Regions[0].IsDraggable, "Drag regions should map unchanged.");
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
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void ProviderBoundaryReturnsResponseOnSuccess()
    {
        var provider = new RecordingResourceProvider();
        var request = new CefGlueNextAvaloniaResourceRequest("app://main/", "GET", IsMainFrame: true, IsMainFrameResource: true);
        var response = CefGlueNextAvaloniaProviderBoundary.SafeResolve(provider, request);
        AssertEqual(200, response.Status, "A successful provider must surface the original 200.");
        Assert(provider.LastRequest is not null, "The boundary must hand the request to the provider unchanged.");
    }

    private static void ProviderBoundaryConvertsExceptionToInternalServerError()
    {
        var provider = new ThrowingResourceProvider();
        var request = new CefGlueNextAvaloniaResourceRequest("app://broken/", "GET", IsMainFrame: false, IsMainFrameResource: false);
        var response = CefGlueNextAvaloniaProviderBoundary.SafeResolve(provider, request);
        AssertEqual(500, response.Status, "A throwing provider must yield a fixed 500 response.");
        AssertEqual("Internal Resource Provider Error", response.StatusText, "The 500 response must carry a stable status text.");
        var body = System.Text.Encoding.UTF8.GetString(response.Content);
        Assert(body.Contains("boom", StringComparison.Ordinal), "The diagnostic body must include the underlying exception message.");
        AssertEqual("no-store", response.CacheControl, "Error responses must opt out of caching to avoid replaying stale 500s.");
    }

    private static void RuntimeOptionsFingerprintStableForEquivalentOptions()
    {
        var a = BuildDeterministicOptions();
        var b = BuildDeterministicOptions();
        AssertEqual(
            CefGlueNextAvaloniaRuntime.ComputeFingerprint(a),
            CefGlueNextAvaloniaRuntime.ComputeFingerprint(b),
            "Equivalent options must yield the same fingerprint for reinit comparison.");
    }

    private static void RuntimeOptionsFingerprintDistinguishesSchemes()
    {
        var a = BuildDeterministicOptions();
        var b = new CefGlueNextAvaloniaRuntimeOptions
        {
            RuntimeDirectory = a.RuntimeDirectory,
            ResourcesDirectory = a.ResourcesDirectory,
            LocalesDirectory = a.LocalesDirectory,
            CacheDirectory = a.CacheDirectory,
            BrowserSubprocessPath = a.BrowserSubprocessPath,
            LogFile = a.LogFile,
            Schemes = new[]
            {
                new CefGlueNextAvaloniaSchemeOptions
                {
                    SchemeName = "app",
                    DomainName = "local",
                    ResourceProvider = new RecordingResourceProvider()
                }
            },
        };
        Assert(
            CefGlueNextAvaloniaRuntime.ComputeFingerprint(a) != CefGlueNextAvaloniaRuntime.ComputeFingerprint(b),
            "Adding a scheme must change the fingerprint so silent reinit drops are rejected.");
    }

    private static void RuntimeOptionsFingerprintDistinguishesSubprocessAndCache()
    {
        var a = BuildDeterministicOptions();
        var b = new CefGlueNextAvaloniaRuntimeOptions
        {
            RuntimeDirectory = a.RuntimeDirectory,
            ResourcesDirectory = a.ResourcesDirectory,
            LocalesDirectory = a.LocalesDirectory,
            CacheDirectory = "different",
            BrowserSubprocessPath = "different-subprocess.exe",
            LogFile = a.LogFile,
            Schemes = a.Schemes,
        };
        Assert(
            CefGlueNextAvaloniaRuntime.ComputeFingerprint(a) != CefGlueNextAvaloniaRuntime.ComputeFingerprint(b),
            "Cache and subprocess paths must be part of the fingerprint.");
    }
    private static CefGlueNextAvaloniaRuntimeOptions BuildDeterministicOptions()
    {
        return new CefGlueNextAvaloniaRuntimeOptions
        {
            RuntimeDirectory = "runtime",
            ResourcesDirectory = "resources",
            LocalesDirectory = "locales",
            CacheDirectory = "cache",
            BrowserSubprocessPath = "subprocess.exe",
            LogFile = "cef.log",
            Schemes = Array.Empty<CefGlueNextAvaloniaSchemeOptions>(),
        };
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

    private sealed class RecordingResourceProvider : ICefGlueNextAvaloniaResourceProvider
    {
        public CefGlueNextAvaloniaResourceRequest? LastRequest { get; private set; }

        public CefGlueNextAvaloniaResourceResponse Resolve(CefGlueNextAvaloniaResourceRequest request)
        {
            LastRequest = request;
            var content = "index"u8.ToArray();
            return new CefGlueNextAvaloniaResourceResponse(
                200,
                "OK",
                "text/html",
                "no-cache",
                content.LongLength,
                content);
        }
    }

    private sealed class ThrowingResourceProvider : ICefGlueNextAvaloniaResourceProvider
    {
        public CefGlueNextAvaloniaResourceResponse Resolve(CefGlueNextAvaloniaResourceRequest request)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("cefglue-next-avalonia-tests-").FullName;

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
