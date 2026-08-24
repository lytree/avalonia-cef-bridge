using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Handlers;

namespace CefGlue.Next.Avalonia;

public sealed class CefGlueNextAvaloniaWebView : ContentControl, IAsyncDisposable
{
    private readonly AvaloniaCefBrowser _browser;
    private readonly CefGlueNextAvaloniaNavigationHandler _navigationHandler;
    private readonly CefGlueNextAvaloniaDownloadHandler _downloadHandler;
    private readonly CefGlueNextAvaloniaDragHandler _dragHandler;
    private readonly TaskCompletionSource<object?> _browserClosed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<object?> _closeCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<CefGlueNextAvaloniaDraggableRegion> _dragRegions = [];
    private Point _lastDragPosition;
    private int _disposeState;
    private bool _addressChangeHooked;

    public CefGlueNextAvaloniaWebView(
        Uri? initialSource = null,
        CefGlueNextAvaloniaRuntimeOptions? runtimeOptions = null)
    {
        CefGlueNextAvaloniaRuntime.Initialize(runtimeOptions);

        Source = ValidateUri(initialSource ?? new Uri("about:blank"));
        HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
        Focusable = true;

        _browser = new AvaloniaCefBrowser();
        _navigationHandler = new CefGlueNextAvaloniaNavigationHandler(this);
        _downloadHandler = new CefGlueNextAvaloniaDownloadHandler(this);
        _dragHandler = new CefGlueNextAvaloniaDragHandler(this);

        _browser.RequestHandler = _navigationHandler;
        _browser.DownloadHandler = _downloadHandler;
        _browser.DragHandler = _dragHandler;
        _browser.WebMessageReceived += OnWebMessageReceived;
        _browser.BrowserInitialized += OnBrowserInitialized;
        _browser.BrowserClosed += OnBrowserClosed;
        _browser.AddressChanged += OnAddressChanged;
        _addressChangeHooked = true;
        _browser.Address = Source.AbsoluteUri;

        Content = _browser;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter, RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Bubble);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);
    }

    public Control BrowserControl => _browser;

    public Uri Source { get; private set; }

    public IReadOnlyList<CefGlueNextAvaloniaDraggableRegion> DragRegions => _dragRegions;

    public event EventHandler? BrowserInitialized;

    public event EventHandler? BrowserClosed;

    public event EventHandler<CefGlueNextAvaloniaNavigationRequestedEventArgs>? NavigationRequested;

    public event EventHandler<CefGlueNextAvaloniaExternalNavigationEventArgs>? ExternalNavigationRequested;

    public event EventHandler<CefGlueNextAvaloniaDownloadRequestedEventArgs>? DownloadRequested;

    public event EventHandler<CefGlueNextAvaloniaFileDropEventArgs>? FileDropEntered;

    public event EventHandler<CefGlueNextAvaloniaFileDropEventArgs>? FileDropped;

    public event EventHandler<CefGlueNextAvaloniaFileDropEventArgs>? FileDropLeft;

    public event EventHandler<CefGlueNextAvaloniaDragRegionsUpdatedEventArgs>? DragRegionsUpdated;

    public event EventHandler<string>? MessageReceived;

    public bool IsBrowserInitialized => _browser.IsBrowserInitialized;

    public bool IsLoading => _browser.IsLoading;

    public string Title => _browser.Title;

    public void Navigate(Uri source)
    {
        ThrowIfDisposed();
        var validated = ValidateUri(source);
        InvokeOnUiThread(() =>
        {
            ThrowIfDisposed();
            Source = validated;
            _browser.Address = Source.AbsoluteUri;
        });
    }

    public void ExecuteScript(string script, string? sourceUrl = null, int line = 1)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        InvokeOnUiThread(() =>
        {
            ThrowIfDisposed();
            _browser.ExecuteJavaScript(script, sourceUrl ?? "about:blank", line);
        });
    }

    public IReadOnlyList<CefGlueNextAvaloniaDraggableRegion> SetDragRegions(
        IReadOnlyList<CefGlueNextAvaloniaDraggableRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        IReadOnlyList<CefGlueNextAvaloniaDraggableRegion> previous = [];
        var next = regions.ToArray();
        InvokeOnUiThread(() =>
        {
            ThrowIfDisposed();
            previous = _dragRegions;
            _dragRegions = next;
        });
        return previous;
    }

    public async ValueTask CloseAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            await _closeCompleted.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                CloseBrowserCore();
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(CloseBrowserCore).GetTask().ConfigureAwait(false);
            }

            await WaitForBrowserCloseAsync().ConfigureAwait(false);
            if (_addressChangeHooked)
            {
                _browser.AddressChanged -= OnAddressChanged;
                _addressChangeHooked = false;
            }
            _browser.BrowserClosed -= OnBrowserClosed;
        }
        finally
        {
            _closeCompleted.TrySetResult(null);
        }
    }

    public void Dispose()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _ = CloseAsync().AsTask();
            return;
        }

        CloseAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync() => CloseAsync();

    internal CefGlueNextAvaloniaNavigationDecision RaiseNavigation(
        Uri uri,
        bool isMainFrame,
        bool userGesture,
        bool isRedirect)
    {
        var args = new CefGlueNextAvaloniaNavigationRequestedEventArgs(uri, isMainFrame, userGesture, isRedirect);
        NavigationRequested?.Invoke(this, args);
        return args.Decision;
    }

    internal void RaiseExternalNavigation(Uri uri)
    {
        var args = new CefGlueNextAvaloniaExternalNavigationEventArgs(uri);
        ExternalNavigationRequested?.Invoke(this, args);
    }

    internal CefGlueNextAvaloniaDownloadRequestedEventArgs? RaiseDownload(Uri uri, string? suggestedFileName)
    {
        var args = new CefGlueNextAvaloniaDownloadRequestedEventArgs(uri, suggestedFileName);
        DownloadRequested?.Invoke(this, args);
        return args;
    }

    internal void RaiseDragRegionsUpdated(IReadOnlyList<CefGlueNextAvaloniaDraggableRegion> regions)
    {
        _dragRegions = regions;
        DragRegionsUpdated?.Invoke(this, new CefGlueNextAvaloniaDragRegionsUpdatedEventArgs(regions));
    }

    internal Point LastDragPosition => _lastDragPosition;

    private void OnBrowserInitialized()
    {
        BrowserInitialized?.Invoke(this, EventArgs.Empty);
    }

    private void OnBrowserClosed()
    {
        _browserClosed.TrySetResult(null);
        BrowserClosed?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddressChanged(object? sender, string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return;
        }

        // Only the main frame's address represents the user-visible Source; sub-frame and
        // resource-load notifications are intentionally ignored.
        Source = uri;
    }

    private void CloseBrowserCore()
    {
        if (_addressChangeHooked)
        {
            _browser.AddressChanged -= OnAddressChanged;
            _addressChangeHooked = false;
        }
        _browser.WebMessageReceived -= OnWebMessageReceived;
        _browser.BrowserInitialized -= OnBrowserInitialized;
        _browser.RequestHandler = null;
        _browser.DownloadHandler = null;
        _browser.DragHandler = null;
        Content = null;

        if (!_browser.IsBrowserInitialized)
        {
            _browser.Dispose();
            _browserClosed.TrySetResult(null);
            _browser.BrowserClosed -= OnBrowserClosed;
            return;
        }

        _browser.Dispose();
    }

    private async Task WaitForBrowserCloseAsync()
    {
        try
        {
            await _browserClosed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // CEF shutdown is asynchronous. Do not hold the Avalonia dispatcher forever when a
            // native browser callback is unavailable during process teardown.
        }
    }

    private static void InvokeOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.InvokeAsync(action).GetTask().GetAwaiter().GetResult();
    }

    private void OnWebMessageReceived(string message)
    {
        MessageReceived?.Invoke(this, message);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        _lastDragPosition = e.GetPosition(this);
        var args = CreateFileDropArgs(e);
        FileDropEntered?.Invoke(this, args);
        e.DragEffects = args.Accepted ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        FileDropLeft?.Invoke(
            this,
            new CefGlueNextAvaloniaFileDropEventArgs([], null, _lastDragPosition));
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _lastDragPosition = e.GetPosition(this);
        var args = CreateFileDropArgs(e);
        FileDropped?.Invoke(this, args);
        e.DragEffects = args.Accepted ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private CefGlueNextAvaloniaFileDropEventArgs CreateFileDropArgs(DragEventArgs e)
    {
        var paths = e.DataTransfer.Contains(DataFormat.File)
            ? e.DataTransfer.TryGetFiles()?.Select(static file => file.Path.LocalPath).ToArray() ?? []
            : [];
        var text = e.DataTransfer.Contains(DataFormat.Text) ? e.DataTransfer.TryGetText() : null;
        return new CefGlueNextAvaloniaFileDropEventArgs(paths, text, _lastDragPosition);
    }

    private static Uri ValidateUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || string.IsNullOrWhiteSpace(uri.Scheme))
        {
            throw new ArgumentException("The WebView URI must be absolute and include a scheme.", nameof(uri));
        }

        return uri;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
    }
}

internal sealed class CefGlueNextAvaloniaNavigationHandler : RequestHandler
{
    private readonly CefGlueNextAvaloniaWebView _owner;

    public CefGlueNextAvaloniaNavigationHandler(CefGlueNextAvaloniaWebView owner)
    {
        _owner = owner;
    }

    protected override bool OnBeforeBrowse(
        CefBrowser browser,
        CefFrame frame,
        CefRequest request,
        bool userGesture,
        bool isRedirect)
    {
        return Resolve(request.Url, frame.IsMain, userGesture, isRedirect);
    }

    protected override bool OnOpenUrlFromTab(
        CefBrowser browser,
        CefFrame frame,
        string targetUrl,
        CefWindowOpenDisposition targetDisposition,
        bool userGesture)
    {
        return Resolve(targetUrl, frame.IsMain, userGesture, false);
    }

    private bool Resolve(string rawUrl, bool isMainFrame, bool userGesture, bool isRedirect)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return true;
        }

        var decision = _owner.RaiseNavigation(uri, isMainFrame, userGesture, isRedirect);
        if (decision == CefGlueNextAvaloniaNavigationDecision.External)
        {
            _owner.RaiseExternalNavigation(uri);
            return true;
        }

        return decision != CefGlueNextAvaloniaNavigationDecision.Allow;
    }
}

internal sealed class CefGlueNextAvaloniaDownloadHandler : DownloadHandler
{
    private readonly CefGlueNextAvaloniaWebView _owner;

    public CefGlueNextAvaloniaDownloadHandler(CefGlueNextAvaloniaWebView owner)
    {
        _owner = owner;
    }

    protected override bool OnBeforeDownload(
        CefBrowser browser,
        CefDownloadItem downloadItem,
        string suggestedName,
        CefBeforeDownloadCallback callback)
    {
        // The callback is IDisposable; if we fail to decide before disposing, CEF retains the
        // native wrapper until the browser is destroyed. Take ownership with a using block so the
        // native ref is released deterministically on every return path, including the early-out
        // when the URL cannot be parsed.
        using (callback)
        {
            if (!Uri.TryCreate(downloadItem.Url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var args = _owner.RaiseDownload(uri, suggestedName);
            if (args?.Decision != CefGlueNextAvaloniaDownloadDecision.Allow)
            {
                return false;
            }

            callback.Continue(args.FilePath, args.ShowDialog);
            return true;
        }
    }
}

internal sealed class CefGlueNextAvaloniaDragHandler : DragHandler
{
    private readonly CefGlueNextAvaloniaWebView _owner;

    public CefGlueNextAvaloniaDragHandler(CefGlueNextAvaloniaWebView owner)
    {
        _owner = owner;
    }

    protected override bool OnDragEnter(CefBrowser browser, CefDragData dragData, CefDragOperationsMask mask)
    {
        // Windowed Avalonia drag events are the source of file coordinates. Returning
        // false keeps Chromium's own drag target handling intact.
        return false;
    }

    protected override void OnDraggableRegionsChanged(CefBrowser browser, CefFrame frame, CefDraggableRegion[] regions)
    {
        var mapped = regions.Select(static region => new CefGlueNextAvaloniaDraggableRegion(
            region.Bounds.X,
            region.Bounds.Y,
            region.Bounds.Width,
            region.Bounds.Height,
            region.Draggable)).ToArray();
        _owner.RaiseDragRegionsUpdated(mapped);
    }
}
