using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tarui.WebView.Abstractions;

namespace Tarui.WebView.CefGlueNext;

public sealed class CefGlueNextWebViewFactory : ITaruiWebViewFactory
{
    public ITaruiWebView Create(TaruiWebViewOptions options) =>
        new CefGlueNextWebView(options);
}

public sealed class CefGlueNextWebView : ITaruiWebView
{
    private readonly TextBlock _status;

    public CefGlueNextWebView(TaruiWebViewOptions options)
    {
        Source = options.InitialSource;
        _status = new TextBlock
        {
            Text = "CefGlue.Next.Avalonia source adapter is ready for the Avalonia 12 port.",
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Control = new Border { Child = _status };
    }

    public Control Control { get; }

    public Uri? Source { get; private set; }

    private EventHandler<TaruiWebMessage>? _messageReceived;

    public event EventHandler<TaruiWebMessage>? MessageReceived
    {
        add => _messageReceived += value;
        remove => _messageReceived -= value;
    }

    public void Navigate(Uri source)
    {
        Source = source;
    }

    public ValueTask<string?> ExecuteScriptAsync(
        string script,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "The CefGlue.Next.Avalonia source port is not enabled yet. " +
            "Set EnableCefGlueNextSourcePort=true after adapting the upstream Avalonia 11.3.14 control to Avalonia 12.");
    }

    public void Dispose()
    {
        _messageReceived = null;
    }

    internal void RaiseMessage(string message) =>
        _messageReceived?.Invoke(this, new TaruiWebMessage(message));
}

public static class CefGlueNextPortInfo
{
    public const string UpstreamRepository = "https://github.com/Deon-Berlin/CefGlue";
    public const string UpstreamCommit = "e3389315dad795374be1a1e52c42d4e49cb6fe7b";
    public const string UpstreamAvaloniaVersion = "11.3.14";
    public const string TargetAvaloniaVersion = "12.1.1";
    public const string CefVersion = "150.0.11";
}
