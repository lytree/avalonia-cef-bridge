using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Tarui.WebView.Abstractions;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.BrowserProcess;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Shared;

namespace Tarui.WebView.CefGlueNext;

public static class CefGlueRuntimeBootstrap
{
    private static int _initialized;

    public static void RunSubProcess(string[] args)
    {
        CefSubProcess.Run(args);
    }

    public static void Initialize(CefGlueNextWebAppOptions webAppOptions)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;

        var runtimeRoot = ResolveRuntimeRoot();
        if (runtimeRoot != null)
        {
            Environment.SetEnvironmentVariable("TARUI_CEF_ROOT", runtimeRoot);
        }
        var resourcesRoot = CefRuntimeLocator.GetResourceDirPath() ?? runtimeRoot;
        var cacheRoot = Path.Combine(Path.GetTempPath(), "tarui.net", "cef", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(cacheRoot);
        CustomScheme[]? customSchemes = null;
        if (webAppOptions.Mode == TaruiWebResourceMode.Scheme)
        {
            customSchemes =
            [
                new CustomScheme
                {
                    SchemeName = webAppOptions.SchemeName,
                    DomainName = webAppOptions.DomainName,
                    IsStandard = true,
                    IsLocal = false,
                    IsDisplayIsolated = true,
                    IsSecure = true,
                    IsCorsEnabled = false,
                    IsCSPBypassing = false,
                    IsFetchEnabled = true,
                    SchemeHandlerFactory = new LocalSchemeHandlerFactory(
                        webAppOptions.ContentRoot!,
                        webAppOptions.SchemeName,
                        webAppOptions.DomainName,
                        webAppOptions.SpaFallback,
                        webAppOptions.ContentSecurityPolicy,
                        webAppOptions.MaxAssetBytes)
                }
            ];
        }

        CefRuntimeLoader.Initialize(
            new CefSettings
            {
                RootCachePath = cacheRoot,
                BrowserSubprocessPath = ResolveSubprocessPath(),
                WindowlessRenderingEnabled = false,
                NoSandbox = true,
                LogSeverity = CefLogSeverity.Warning,
                LogFile = Path.Combine(cacheRoot, "cef.log"),
                ResourcesDirPath = resourcesRoot,
                LocalesDirPath = ResolveLocalesRoot(runtimeRoot, resourcesRoot)
            },
            flags:
            [
                new KeyValuePair<string, string>("do-not-de-elevate", string.Empty)
            ],
            customSchemes: customSchemes);
    }

    private static string? ResolveSubprocessPath()
    {
        var fileName = OperatingSystem.IsWindows() ? "tarui.net.exe" : "tarui.net";
        var appHost = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(appHost) ? appHost : Environment.ProcessPath;
    }

    private static string? ResolveLocalesRoot(string? runtimeRoot, string? resourcesRoot)
    {
        if (resourcesRoot != null)
        {
            var adjacent = Path.Combine(resourcesRoot, "locales");
            if (Directory.Exists(adjacent)) return adjacent;
        }

        if (runtimeRoot == null) return null;
        try
        {
            return Directory.EnumerateDirectories(runtimeRoot, "locales", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveRuntimeRoot()
    {
        var configured = Environment.GetEnvironmentVariable("TARUI_CEF_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var rid = OperatingSystem.IsWindows()
            ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64")
            : OperatingSystem.IsMacOS()
                ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64")
                : (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64");
        var bundled = Path.Combine(AppContext.BaseDirectory, "CEF", rid);
        return Directory.Exists(bundled) ? bundled : null;
    }
}

public sealed class CefGlueNextWebViewFactory : ITaruiWebViewFactory
{
    public CefGlueNextWebViewFactory(CefGlueNextWebAppOptions webAppOptions)
    {
        CefGlueRuntimeBootstrap.Initialize(webAppOptions);
    }

    public ITaruiWebView Create(TaruiWebViewOptions options) =>
        new CefGlueNextWebView(options);
}

public sealed class CefGlueNextWebView : ITaruiWebView
{
    private readonly AvaloniaCefBrowser _browser;
    private EventHandler<TaruiWebMessage>? _messageReceived;

    public CefGlueNextWebView(TaruiWebViewOptions options)
    {
        Source = options.InitialSource;
        _browser = new AvaloniaCefBrowser();
        _browser.WebMessageReceived += OnWebMessageReceived;
        _browser.Address = options.InitialSource.AbsoluteUri;
        Control = _browser;
    }

    public Control Control { get; }

    public Uri? Source { get; private set; }

    public event EventHandler<TaruiWebMessage>? MessageReceived
    {
        add => _messageReceived += value;
        remove => _messageReceived -= value;
    }

    public void Navigate(Uri source)
    {
        Source = source;
        Dispatcher.UIThread.Post(() => _browser.Address = source.AbsoluteUri);
    }

    public async ValueTask<string?> ExecuteScriptAsync(
        string script,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
            _browser.ExecuteJavaScript(script)).GetTask();
        return null;
    }

    public void Dispose()
    {
        _browser.WebMessageReceived -= OnWebMessageReceived;
        _messageReceived = null;
        _browser.Dispose();
    }

    private void OnWebMessageReceived(string message) =>
        _messageReceived?.Invoke(this, new TaruiWebMessage(message));
}

public static class CefGlueNextPortInfo
{
    public const string UpstreamCommit = "e3389315dad795374be1a1e52c42d4e49cb6fe7b";
    public const string UpstreamAvaloniaVersion = "11.3.17";
    public const string TargetAvaloniaVersion = "12.1.1";
    public const string CefVersion = "150.0.11";
}
