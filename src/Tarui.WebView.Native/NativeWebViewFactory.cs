using Avalonia.Controls;
using Tarui.WebView.Abstractions;

namespace Tarui.WebView.Native;

public sealed class NativeWebViewFactory : ITaruiWebViewFactory
{
    public ITaruiWebView Create(TaruiWebViewOptions options) => new NativeWebViewAdapter(options);
}

internal sealed class NativeWebViewAdapter : ITaruiWebView
{
    private readonly NativeWebView _webView;

    public NativeWebViewAdapter(TaruiWebViewOptions options)
    {
        _webView = new NativeWebView { Source = options.InitialSource };
        _webView.WebMessageReceived += OnWebMessageReceived;
        Source = options.InitialSource;
    }

    public Control Control => _webView;
    public Uri? Source { get; private set; }
    public event EventHandler<TaruiWebMessage>? MessageReceived;

    public void Navigate(Uri source)
    {
        Source = source;
        _webView.Navigate(source);
    }

    public async ValueTask<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _webView.InvokeScript(script);
    }

    public void Dispose() => _webView.WebMessageReceived -= OnWebMessageReceived;

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs args) =>
        MessageReceived?.Invoke(this, new TaruiWebMessage(args.Body ?? string.Empty));
}
