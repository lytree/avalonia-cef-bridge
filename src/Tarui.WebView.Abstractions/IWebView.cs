using Avalonia.Controls;

namespace Tarui.WebView.Abstractions;

public sealed record TaruiWebViewOptions(Uri InitialSource);

public sealed record TaruiWebMessage(string Message);

public interface ITaruiWebView : IDisposable
{
    Control Control { get; }

    Uri? Source { get; }

    event EventHandler<TaruiWebMessage>? MessageReceived;

    void Navigate(Uri source);

    ValueTask<string?> ExecuteScriptAsync(
        string script,
        CancellationToken cancellationToken = default);
}

public interface ITaruiWebViewFactory
{
    ITaruiWebView Create(TaruiWebViewOptions options);
}
