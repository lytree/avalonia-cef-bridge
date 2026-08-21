using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.WebView.Abstractions;

namespace Tarui.Shell;

/// <summary>
/// Hosts an <see cref="ITaruiWebView"/> inside a window and translates its typed native events into
/// reserved <c>window://</c> and <c>webview://</c> events scoped to the owning window. Navigation and
/// download requests are first resolved by <see cref="WebViewRequestPolicy"/>; the window only receives
/// file-path and webview payloads when its capability <c>events</c> authorize the reserved event. A drag
/// is rejected up front when the window may not receive the drop, so un-authorized windows never carry
/// the drop payload at the OS level.
/// </summary>
public sealed class WebViewHost : Border, IEventSink, IDisposable
{
    private const string FileDropEnteredEvent = "window://file-drop-entered";
    private const string FileDropLeftEvent = "window://file-drop-left";
    private const string FileDroppedEvent = "window://file-dropped";
    private const string DownloadRequestedEvent = "webview://download-requested";
    private const string NavigationRequestedEvent = "webview://navigation-requested";

    private readonly IpcDispatcher _dispatcher;
    private readonly EventRouter _eventRouter;
    private readonly WebViewRequestPolicy _policy;
    private readonly CommandContext _context;
    private readonly string _label;
    private readonly ITaruiWebView _webView;

    public WebViewHost(
        ITaruiWebViewFactory webViewFactory,
        IpcDispatcher dispatcher,
        EventRouter eventRouter,
        WebViewRequestPolicy policy,
        CommandContext context,
        Uri source)
    {
        _dispatcher = dispatcher;
        _eventRouter = eventRouter;
        _policy = policy;
        _context = context;
        _label = context.WindowLabel;
        _webView = webViewFactory.Create(new TaruiWebViewOptions(source));
        _webView.MessageReceived += OnMessageReceived;
        _webView.FileDropEntered += OnFileDropEntered;
        _webView.FileDropLeft += OnFileDropLeft;
        _webView.FileDropped += OnFileDropped;
        _webView.DownloadRequested += OnDownloadRequested;
        _webView.NavigationRequested += OnNavigationRequested;
        Child = _webView.Control;
    }

    public async ValueTask<string> DispatchMessageAsync(
        string json,
        CancellationToken cancellationToken = default)
    {
        var response = await _dispatcher.DispatchJsonAsync(json, _context, cancellationToken);
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

    private void OnFileDropEntered(object? sender, TaruiWebViewFileDropEventArgs args)
    {
        // Reject the OS drag up front when this window cannot receive file paths.
        args.Accepted = MayReceive(FileDropEnteredEvent);
        if (!args.Accepted)
        {
            return;
        }

        FireAndForget(_eventRouter.EmitToWindowAsync(
            _label,
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

        FireAndForget(_eventRouter.EmitToWindowAsync(
            _label,
            FileDropLeftEvent,
            JsonSerializer.SerializeToElement(new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs)));
    }

    private void OnFileDropped(object? sender, TaruiWebViewFileDropEventArgs args)
    {
        if (!MayReceive(FileDroppedEvent))
        {
            return;
        }

        FireAndForget(_eventRouter.EmitToWindowAsync(
            _label,
            FileDroppedEvent,
            JsonSerializer.SerializeToElement(
                new WebViewFileDropEvent(args.Paths, args.Text, args.X, args.Y),
                TaruiJsonContext.Default.WebViewFileDropEvent)));
    }

    private void OnDownloadRequested(object? sender, TaruiWebViewDownloadEventArgs args)
    {
        var decision = _policy.DecideDownload(new Uri(args.Url, UriKind.Absolute));
        args.Decision = decision == WebViewRequestDecision.Allow
            ? TaruiWebViewDownloadAction.Allow
            : TaruiWebViewDownloadAction.Deny;

        if (decision != WebViewRequestDecision.Allow || !MayReceive(DownloadRequestedEvent))
        {
            return;
        }

        FireAndForget(_eventRouter.EmitToWindowAsync(
            _label,
            DownloadRequestedEvent,
            JsonSerializer.SerializeToElement(
                new WebViewDownloadRequestEvent(args.Url, args.SuggestedFilename),
                TaruiJsonContext.Default.WebViewDownloadRequestEvent)));
    }

    private void OnNavigationRequested(object? sender, TaruiWebViewNavigationEventArgs args)
    {
        args.Decision = ToAction(_policy.DecideNavigation(args.Url));
        if (args.Decision == TaruiWebViewNavigationAction.Deny || !MayReceive(NavigationRequestedEvent))
        {
            return;
        }

        FireAndForget(_eventRouter.EmitToWindowAsync(
            _label,
            NavigationRequestedEvent,
            JsonSerializer.SerializeToElement(
                new WebViewNavigationRequestEvent(args.Url.AbsoluteUri, args.IsMainFrame),
                TaruiJsonContext.Default.WebViewNavigationRequestEvent)));
    }

    private bool MayReceive(string eventName) => _context.Capabilities.AllowsEvent(eventName);

    private static TaruiWebViewNavigationAction ToAction(WebViewRequestDecision decision) => decision switch
    {
        WebViewRequestDecision.Allow => TaruiWebViewNavigationAction.Allow,
        WebViewRequestDecision.External => TaruiWebViewNavigationAction.External,
        _ => TaruiWebViewNavigationAction.Deny,
    };

    private async void OnMessageReceived(object? sender, TaruiWebMessage message)
    {
        try
        {
            await DispatchMessageAsync(message.Message);
        }
        catch
        {
            // Event handlers cannot surface a failed response to the WebView.
        }
    }

    private static async void FireAndForget(ValueTask task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Native web view events are best-effort notifications.
        }
    }

    public void Dispose()
    {
        _webView.MessageReceived -= OnMessageReceived;
        _webView.FileDropEntered -= OnFileDropEntered;
        _webView.FileDropLeft -= OnFileDropLeft;
        _webView.FileDropped -= OnFileDropped;
        _webView.DownloadRequested -= OnDownloadRequested;
        _webView.NavigationRequested -= OnNavigationRequested;
        _webView.Dispose();
    }
}