using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.WebView.Abstractions;

namespace Tarui.Shell;

public sealed class WebViewHost : Border, IDisposable
{
    private readonly IpcDispatcher _dispatcher;
    private readonly CommandContext _context;
    private readonly ITaruiWebView _webView;

    public WebViewHost(
        ITaruiWebViewFactory webViewFactory,
        IpcDispatcher dispatcher,
        CommandContext context,
        Uri source)
    {
        _dispatcher = dispatcher;
        _context = context;
        _webView = webViewFactory.Create(new TaruiWebViewOptions(source));
        _webView.MessageReceived += OnMessageReceived;
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

    public void Dispose()
    {
        _webView.MessageReceived -= OnMessageReceived;
        _webView.Dispose();
    }
}
