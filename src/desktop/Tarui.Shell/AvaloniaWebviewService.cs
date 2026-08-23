using Avalonia.Threading;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Webview;
using Tarui.WebView.Abstractions;

namespace Tarui.Shell;

/// <summary>
/// Shell-backed <see cref="IWebviewService"/>. A web view is resolved from the live web view session
/// owned by the window with the matching label (web view and window share one label while windows host
/// a single surface). Navigations run on the UI thread and are confined to the application origin's
/// accepted schemes — HTTP(S) and, when local assets are served, the portless custom app scheme.
/// </summary>
public sealed class AvaloniaWebviewService(WindowRegistry registry, TaruiAppOrigin appOrigin) : IWebviewService
{
    public async ValueTask<Unit> NavigateAsync(string webviewLabel, string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = Resolve(webviewLabel, url);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var entry = registry.Get(webviewLabel);
            var session = entry.Webview
                ?? throw new InvalidOperationException($"No web view session is mounted for '{webviewLabel}'.");
            session.WebView.Navigate(target);
        });
        return new Unit();
    }

    public async ValueTask<WebviewStateInfo> GetStateAsync(string webviewLabel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var entry = registry.Get(webviewLabel);
            var session = entry.Webview;
            return new WebviewStateInfo(
                webviewLabel,
                entry.Context.WindowLabel,
                session?.WebView.Source?.AbsoluteUri,
                entry.Window.Title ?? string.Empty);
        });
    }

    public ValueTask<string[]> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // One web view per window today, so live web views mirror registered windows.
        return ValueTask.FromResult(registry.Labels.ToArray());
    }

    private Uri Resolve(string webviewLabel, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException($"A URL is required to navigate web view '{webviewLabel}'.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            throw new InvalidOperationException($"The URL '{url}' is not an absolute URI.");
        }

        if (!appOrigin.AllowsScheme(absolute.Scheme))
        {
            throw new InvalidOperationException(
                $"The URL scheme '{absolute.Scheme}' is not one of the application schemes " +
                $"({string.Join(", ", appOrigin.Schemes)}).");
        }

        return absolute;
    }
}