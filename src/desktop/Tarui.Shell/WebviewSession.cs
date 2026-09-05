using System.Text;
using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.WebView.Abstractions;
using Tarui.WebView.Avalonia;

namespace Tarui.Shell;

/// <summary>
/// The UI-neutral session that drives a single web view surface: it owns the <see cref="ITaruiAvaloniaWebView"/>,
/// dispatches incoming messages and reserved <c>window://</c>/<c>webview://</c> events for its owning window,
/// and applies the navigation/download/capability policy to native web view requests.
/// <para>
/// This type deliberately references no Avalonia visual control: it is a pure logic node so a window can be
/// shown, reused or host several sessions independently of how their surfaces are presented. The visual host
/// (<see cref="WebviewPresenter"/>) only binds the session's <see cref="ITaruiWebView"/> control.
/// </para>
/// </summary>
public sealed class WebviewSession : IEventSink, IChannelSink, IDisposable, IAsyncDisposable
{
    private const string FileDropEnteredEvent = "window://file-drop-entered";
    private const string FileDropLeftEvent = "window://file-drop-left";
    private const string FileDroppedEvent = "window://file-dropped";
    private const string DownloadRequestedEvent = "webview://download-requested";
    private const string NavigationRequestedEvent = "webview://navigation-requested";
    private const string BridgeErrorCode = "BRIDGE_ERROR";

    private readonly IpcDispatcher _dispatcher;
    private readonly EventRouter _eventRouter;
    private readonly WebViewRequestPolicy _policy;
    private readonly ITaruiAvaloniaWebView _webView;
    private int _disposeState;

    /// <summary>The underlying web view and the Avalonia surface it carries.</summary>
    public ITaruiAvaloniaWebView WebView => _webView;

    /// <summary>The window the session belongs to.</summary>
    public string Label => Context.WindowLabel;

    /// <summary>The capability-scoped command context used to authorize IPC and reserved events.</summary>
    public CommandContext Context { get; }

    public WebviewSession(
        ITaruiAvaloniaWebViewFactory webViewFactory,
        IpcDispatcher dispatcher,
        EventRouter eventRouter,
        WebViewRequestPolicy policy,
        CommandContext context,
        Uri source)
    {
        _dispatcher = dispatcher;
        _eventRouter = eventRouter;
        _policy = policy;
        Context = context;
        _webView = webViewFactory.Create(new TaruiWebViewOptions(source));
        _webView.MessageReceived += OnMessageReceived;
        _webView.FileDropEntered += OnFileDropEntered;
        _webView.FileDropLeft += OnFileDropLeft;
        _webView.FileDropped += OnFileDropped;
        _webView.DownloadRequested += OnDownloadRequested;
        _webView.NavigationRequested += OnNavigationRequested;
    }

    public async ValueTask<string> DispatchMessageAsync(
        string json,
        CancellationToken cancellationToken = default)
    {
        var response = await _dispatcher.DispatchJsonAsync(
            json,
            Context,
            channelSink: this,
            cancellationToken: cancellationToken);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(response));
        await _webView.ExecuteScriptAsync($"window.__tarui_dispatchBase64?.('{encoded}')", cancellationToken);
        return response;
    }

    public async ValueTask SendEventAsync(
        string eventName,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = new EventEnvelope("event", eventName, payload);
        var json = JsonSerializer.Serialize(envelope, TaruiJsonContext.Default.EventEnvelope);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        await _webView.ExecuteScriptAsync($"window.__tarui_dispatchBase64?.('{encoded}')", cancellationToken);
    }

    public async ValueTask SendAsync(
        string channelId,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = new ChannelEnvelope("channel", channelId, payload);
        var json = JsonSerializer.Serialize(envelope, TaruiJsonContext.Default.ChannelEnvelope);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        await _webView.ExecuteScriptAsync($"window.__tarui_dispatchBase64?.('{encoded}')", cancellationToken);
    }

    private void OnFileDropEntered(object? sender, TaruiWebViewFileDropEventArgs args)
    {
        // Reject the OS drag up front when this window cannot receive file paths.
        args.Accepted = MayReceive(FileDropEnteredEvent);
        if (!args.Accepted)
        {
            return;
        }

        FireAndForget.Run(_eventRouter.EmitToWindowAsync(
            Label,
            FileDropEnteredEvent,
            JsonSerializer.SerializeToElement(
                new WebViewFileDropEvent(args.Paths, args.Text, args.X, args.Y),
                TaruiJsonContext.Default.WebViewFileDropEvent)));
    }

    private void OnFileDropLeft(object? sender, TaruiWebViewFileDropLeftEventArgs args)
    {
        if (!MayReceive(FileDropLeftEvent))
        {
            return;
        }

        FireAndForget.Run(_eventRouter.EmitToWindowAsync(
            Label,
            FileDropLeftEvent,
            JsonSerializer.SerializeToElement(new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs)));
    }

    private void OnFileDropped(object? sender, TaruiWebViewFileDropEventArgs args)
    {
        if (!MayReceive(FileDroppedEvent))
        {
            return;
        }

        FireAndForget.Run(_eventRouter.EmitToWindowAsync(
            Label,
            FileDroppedEvent,
            JsonSerializer.SerializeToElement(
                new WebViewFileDropEvent(args.Paths, args.Text, args.X, args.Y),
                TaruiJsonContext.Default.WebViewFileDropEvent)));
    }

    private void OnDownloadRequested(object? sender, TaruiWebViewDownloadEventArgs args)
    {
        if (!Uri.TryCreate(args.Url, UriKind.Absolute, out var url))
        {
            args.Decision = TaruiWebViewDownloadAction.Deny;
            return;
        }

        var decision = DecideDownload(url);
        args.Decision = decision == WebViewRequestDecision.Allow
            ? TaruiWebViewDownloadAction.Allow
            : TaruiWebViewDownloadAction.Deny;

        if (decision != WebViewRequestDecision.Allow || !MayReceive(DownloadRequestedEvent))
        {
            args.Decision = TaruiWebViewDownloadAction.Deny;
            return;
        }

        FireAndForget.Run(_eventRouter.EmitToWebviewAsync(
            Label,
            DownloadRequestedEvent,
            JsonSerializer.SerializeToElement(
                new WebViewDownloadRequestEvent(args.Url, args.SuggestedFilename),
                TaruiJsonContext.Default.WebViewDownloadRequestEvent)));
    }

    private void OnNavigationRequested(object? sender, TaruiWebViewNavigationEventArgs args)
    {
        args.Decision = ToAction(DecideNavigation(args.Url));
        if (args.Decision == TaruiWebViewNavigationAction.Deny || !MayReceive(NavigationRequestedEvent))
        {
            args.Decision = TaruiWebViewNavigationAction.Deny;
            return;
        }

        FireAndForget.Run(_eventRouter.EmitToWebviewAsync(
            Label,
            NavigationRequestedEvent,
            JsonSerializer.SerializeToElement(
                new WebViewNavigationRequestEvent(args.Url.AbsoluteUri, args.IsMainFrame),
                TaruiJsonContext.Default.WebViewNavigationRequestEvent)));
    }

    private bool MayReceive(string eventName) => Context.Capabilities.AllowsEvent(eventName);

    private WebViewRequestDecision DecideNavigation(Uri url)
    {
        try
        {
            return _policy.DecideNavigation(url);
        }
        catch (WebViewRequestDeniedException)
        {
            // Malformed or unsafe-scheme URLs (for example the initial about:blank page CEF loads before
            // the real start URL) are cancelled as a plain deny instead of crashing the native callback.
            return WebViewRequestDecision.Deny;
        }
    }

    private WebViewRequestDecision DecideDownload(Uri url)
    {
        try
        {
            return _policy.DecideDownload(url);
        }
        catch (WebViewRequestDeniedException)
        {
            return WebViewRequestDecision.Deny;
        }
    }

    private static TaruiWebViewNavigationAction ToAction(WebViewRequestDecision decision) => decision switch
    {
        WebViewRequestDecision.Allow => TaruiWebViewNavigationAction.Allow,
        WebViewRequestDecision.External => TaruiWebViewNavigationAction.External,
        _ => TaruiWebViewNavigationAction.Deny,
    };

    private void OnMessageReceived(object? sender, TaruiWebMessage message)
    {
        // The web view event marshals to a worker thread; we cannot await inline so we schedule the
        // dispatcher coroutine through the shared FireAndForget helper and bridge failures back to
        // the renderer with a BRIDGE_ERROR frame so the front-end never silently times out.
        FireAndForget.Run(DispatchAndReportAsync(message.Message));
    }

    private async ValueTask DispatchAndReportAsync(string json)
    {
        try
        {
            await DispatchMessageAsync(json).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The dispatcher normally converts malformed payloads into InvokeResponse.Fail(...) but
            // catastrophic errors (renderer crash, base64 failure, native callback throw) still leak
            // through this catch. Synthesize a bridge error and push it back so the front-end can
            // surface the failure instead of waiting 30s for a timeout.
            await TryPushBridgeErrorAsync(json, exception).ConfigureAwait(false);
        }
    }

    private async ValueTask TryPushBridgeErrorAsync(string json, Exception exception)
    {
        try
        {
            var id = ExtractRequestId(json) ?? "unknown";
            var response = InvokeResponse.Fail(id, BridgeErrorCode, exception.Message);
            var payload = JsonSerializer.Serialize(response, TaruiJsonContext.Default.InvokeResponse);
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            await _webView
                .ExecuteScriptAsync($"window.__tarui_dispatchBase64?.('{encoded}')")
                .ConfigureAwait(false);
        }
        catch
        {
            // The web view is gone or its renderer has crashed; nothing more we can do.
        }
    }

    private static string? ExtractRequestId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("id", out var idElement) &&
                idElement.ValueKind == JsonValueKind.String)
            {
                return idElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Fall through; the dispatcher will already have rejected the payload.
        }
        return null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        DetachWebViewEvents();
        _webView.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        DetachWebViewEvents();
        if (_webView is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _webView.Dispose();
        }
    }

    private void DetachWebViewEvents()
    {
        _webView.MessageReceived -= OnMessageReceived;
        _webView.FileDropEntered -= OnFileDropEntered;
        _webView.FileDropLeft -= OnFileDropLeft;
        _webView.FileDropped -= OnFileDropped;
        _webView.DownloadRequested -= OnDownloadRequested;
        _webView.NavigationRequested -= OnNavigationRequested;
    }
}
