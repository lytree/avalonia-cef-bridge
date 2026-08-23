using System.Diagnostics;
using Avalonia.Controls;
using CefGlue.Next.Avalonia;
using Tarui.WebView.Abstractions;
using Tarui.WebView.Avalonia;

namespace Tarui.WebView.CefGlueNext;

public static class CefGlueRuntimeBootstrap
{
    public static void RunSubProcess(string[] args)
    {
        CefGlueNextAvaloniaRuntime.RunSubProcess(args);
    }

    public static void Initialize(CefGlueNextWebAppOptions webAppOptions)
    {
        ArgumentNullException.ThrowIfNull(webAppOptions);

        CefGlueNextAvaloniaRuntime.Initialize(
            new CefGlueNextAvaloniaRuntimeOptions
            {
                RuntimeDirectory = ResolveRuntimeRoot(),
                CacheDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "tarui.net",
                    "cef",
                    Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Schemes = webAppOptions.Mode == TaruiWebResourceMode.Scheme
                    ?
                    [
                        new CefGlueNextAvaloniaSchemeOptions
                        {
                            SchemeName = webAppOptions.SchemeName,
                            DomainName = webAppOptions.DomainName,
                            IsStandard = true,
                            IsDisplayIsolated = true,
                            IsSecure = true,
                            IsCorsEnabled = false,
                            IsCspBypassing = false,
                            IsFetchEnabled = true,
                            ResourceProvider = new LocalWebAssetResolver(
                                webAppOptions.ContentRoot!,
                                webAppOptions.SchemeName,
                                webAppOptions.DomainName,
                                webAppOptions.SpaFallback,
                                webAppOptions.MaxAssetBytes,
                                webAppOptions.ContentSecurityPolicy)
                        }
                    ]
                    : []
            });
    }

    private static string? ResolveRuntimeRoot()
    {
        var configured = Environment.GetEnvironmentVariable("TARUI_CEF_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        var architectureName = architecture == System.Runtime.InteropServices.Architecture.Arm64
            ? "arm64"
            : "x64";
        var platform = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var bundled = Path.Combine(AppContext.BaseDirectory, "CEF", $"{platform}-{architectureName}");
        return Directory.Exists(bundled) ? bundled : null;
    }
}

public sealed class CefGlueNextWebViewFactory : ITaruiWebViewFactory, ITaruiAvaloniaWebViewFactory
{
    public CefGlueNextWebViewFactory(CefGlueNextWebAppOptions webAppOptions)
    {
        ArgumentNullException.ThrowIfNull(webAppOptions);
        CefGlueRuntimeBootstrap.Initialize(webAppOptions);
    }

    ITaruiWebView ITaruiWebViewFactory.Create(TaruiWebViewOptions options) => Create(options);

    ITaruiAvaloniaWebView ITaruiAvaloniaWebViewFactory.Create(TaruiWebViewOptions options) => Create(options);

    public CefGlueNextWebView Create(TaruiWebViewOptions options)
    {
        GC.KeepAlive(this);
        return new CefGlueNextWebView(options);
    }
}

public sealed class CefGlueNextWebView : ITaruiAvaloniaWebView, IAsyncDisposable
{
    private readonly CefGlueNextAvaloniaWebView _component;
    private EventHandler<TaruiWebMessage>? _messageReceived;
    private EventHandler<TaruiWebViewFileDropEventArgs>? _fileDropEntered;
    private EventHandler<TaruiWebViewFileDropLeftEventArgs>? _fileDropLeft;
    private EventHandler<TaruiWebViewFileDropEventArgs>? _fileDropped;
    private EventHandler<TaruiWebViewDownloadEventArgs>? _downloadRequested;
    private EventHandler<TaruiWebViewNavigationEventArgs>? _navigationRequested;
    private EventHandler<TaruiWebViewDragRegionEventArgs>? _dragRegionsUpdated;
    private int _disposeState;

    public CefGlueNextWebView(TaruiWebViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _component = new CefGlueNextAvaloniaWebView(options.InitialSource);
        _component.MessageReceived += OnComponentMessageReceived;
        _component.FileDropEntered += OnComponentFileDropEntered;
        _component.FileDropLeft += OnComponentFileDropLeft;
        _component.FileDropped += OnComponentFileDropped;
        _component.DownloadRequested += OnComponentDownloadRequested;
        _component.NavigationRequested += OnComponentNavigationRequested;
        _component.ExternalNavigationRequested += OnComponentExternalNavigationRequested;
        _component.DragRegionsUpdated += OnComponentDragRegionsUpdated;
    }

    public Control Control => _component;

    public Uri? Source => _component.Source;

    public event EventHandler<TaruiWebMessage>? MessageReceived
    {
        add => _messageReceived += value;
        remove => _messageReceived -= value;
    }

    public event EventHandler<TaruiWebViewFileDropEventArgs>? FileDropEntered
    {
        add => _fileDropEntered += value;
        remove => _fileDropEntered -= value;
    }

    public event EventHandler<TaruiWebViewFileDropLeftEventArgs>? FileDropLeft
    {
        add => _fileDropLeft += value;
        remove => _fileDropLeft -= value;
    }

    public event EventHandler<TaruiWebViewFileDropEventArgs>? FileDropped
    {
        add => _fileDropped += value;
        remove => _fileDropped -= value;
    }

    public event EventHandler<TaruiWebViewDownloadEventArgs>? DownloadRequested
    {
        add => _downloadRequested += value;
        remove => _downloadRequested -= value;
    }

    public event EventHandler<TaruiWebViewNavigationEventArgs>? NavigationRequested
    {
        add => _navigationRequested += value;
        remove => _navigationRequested -= value;
    }

    public event EventHandler<TaruiWebViewDragRegionEventArgs>? DragRegionsUpdated
    {
        add => _dragRegionsUpdated += value;
        remove => _dragRegionsUpdated -= value;
    }

    public void Navigate(Uri source) => _component.Navigate(source);

    public async ValueTask<string?> ExecuteScriptAsync(
        string script,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _component.ExecuteScript(script);
        await ValueTask.CompletedTask;
        return null;
    }

    public IReadOnlyList<DraggableRegion> SetDragRegions(IReadOnlyList<DraggableRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var mapped = regions
            .Select(static region => new CefGlueNextAvaloniaDraggableRegion(
                checked((int)region.X),
                checked((int)region.Y),
                checked((int)region.Width),
                checked((int)region.Height),
                region.Kind == DraggableRegionKind.Drag))
            .ToArray();
        var previous = _component.SetDragRegions(mapped);
        return previous
            .Select(static region => new DraggableRegion(
                region.X,
                region.Y,
                region.Width,
                region.Height,
                region.IsDraggable ? DraggableRegionKind.Drag : DraggableRegionKind.NoDrag))
            .ToArray();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _component.MessageReceived -= OnComponentMessageReceived;
        _component.FileDropEntered -= OnComponentFileDropEntered;
        _component.FileDropLeft -= OnComponentFileDropLeft;
        _component.FileDropped -= OnComponentFileDropped;
        _component.DownloadRequested -= OnComponentDownloadRequested;
        _component.NavigationRequested -= OnComponentNavigationRequested;
        _component.ExternalNavigationRequested -= OnComponentExternalNavigationRequested;
        _component.DragRegionsUpdated -= OnComponentDragRegionsUpdated;
        _component.Dispose();
        _messageReceived = null;
        _fileDropEntered = null;
        _fileDropLeft = null;
        _fileDropped = null;
        _downloadRequested = null;
        _navigationRequested = null;
        _dragRegionsUpdated = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        DetachComponentEvents();
        await _component.CloseAsync();
        ClearEventHandlers();
    }

    private void DetachComponentEvents()
    {
        _component.MessageReceived -= OnComponentMessageReceived;
        _component.FileDropEntered -= OnComponentFileDropEntered;
        _component.FileDropLeft -= OnComponentFileDropLeft;
        _component.FileDropped -= OnComponentFileDropped;
        _component.DownloadRequested -= OnComponentDownloadRequested;
        _component.NavigationRequested -= OnComponentNavigationRequested;
        _component.ExternalNavigationRequested -= OnComponentExternalNavigationRequested;
        _component.DragRegionsUpdated -= OnComponentDragRegionsUpdated;
    }

    private void ClearEventHandlers()
    {
        _messageReceived = null;
        _fileDropEntered = null;
        _fileDropLeft = null;
        _fileDropped = null;
        _downloadRequested = null;
        _navigationRequested = null;
        _dragRegionsUpdated = null;
    }

    private void OnComponentMessageReceived(object? sender, string message) =>
        _messageReceived?.Invoke(this, new TaruiWebMessage(message));

    private void OnComponentFileDropEntered(object? sender, CefGlueNextAvaloniaFileDropEventArgs args)
    {
        var mapped = new TaruiWebViewFileDropEventArgs(
            args.Paths.ToArray(),
            args.Text,
            args.Position.X,
            args.Position.Y);
        _fileDropEntered?.Invoke(this, mapped);
        args.Accepted = mapped.Accepted;
    }

    private void OnComponentFileDropLeft(object? sender, CefGlueNextAvaloniaFileDropEventArgs args) =>
        _fileDropLeft?.Invoke(this, TaruiWebViewFileDropLeftEventArgs.Instance);

    private void OnComponentFileDropped(object? sender, CefGlueNextAvaloniaFileDropEventArgs args)
    {
        var mapped = new TaruiWebViewFileDropEventArgs(
            args.Paths.ToArray(),
            args.Text,
            args.Position.X,
            args.Position.Y);
        _fileDropped?.Invoke(this, mapped);
        args.Accepted = mapped.Accepted;
    }

    private void OnComponentDownloadRequested(
        object? sender,
        CefGlueNextAvaloniaDownloadRequestedEventArgs args)
    {
        var mapped = new TaruiWebViewDownloadEventArgs(args.Uri.AbsoluteUri, args.SuggestedFileName);
        _downloadRequested?.Invoke(this, mapped);
        args.Decision = mapped.Decision == TaruiWebViewDownloadAction.Allow
            ? CefGlueNextAvaloniaDownloadDecision.Allow
            : CefGlueNextAvaloniaDownloadDecision.Deny;
    }

    private void OnComponentNavigationRequested(
        object? sender,
        CefGlueNextAvaloniaNavigationRequestedEventArgs args)
    {
        var mapped = new TaruiWebViewNavigationEventArgs(args.Uri, args.IsMainFrame);
        _navigationRequested?.Invoke(this, mapped);
        args.Decision = mapped.Decision switch
        {
            TaruiWebViewNavigationAction.Allow => CefGlueNextAvaloniaNavigationDecision.Allow,
            TaruiWebViewNavigationAction.External => CefGlueNextAvaloniaNavigationDecision.External,
            _ => CefGlueNextAvaloniaNavigationDecision.Deny
        };
    }

    private void OnComponentExternalNavigationRequested(
        object? sender,
        CefGlueNextAvaloniaExternalNavigationEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
            args.Handled = true;
        }
        catch
        {
            args.Handled = false;
        }
    }

    private void OnComponentDragRegionsUpdated(
        object? sender,
        CefGlueNextAvaloniaDragRegionsUpdatedEventArgs args)
    {
        var regions = args.Regions
            .Select(static region => new DraggableRegion(
                region.X,
                region.Y,
                region.Width,
                region.Height,
                region.IsDraggable ? DraggableRegionKind.Drag : DraggableRegionKind.NoDrag))
            .ToArray();
        _dragRegionsUpdated?.Invoke(this, new TaruiWebViewDragRegionEventArgs(regions));
    }
}

public static class CefGlueNextPortInfo
{
    public const string UpstreamCommit = "e3389315dad795374be1a1e52c42d4e49cb6fe7b";
    public const string UpstreamAvaloniaVersion = "11.3.17";
    public const string TargetAvaloniaVersion = "12.1.1";
    public const string CefVersion = "150.0.11";
}
