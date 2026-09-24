using Microsoft.AspNetCore.Components.WebView;
using Tarui.WebView.Abstractions;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// Shared state between the Blazor Hybrid pieces: the live <see cref="WebViewManager"/> and the
/// web view it is currently bound to. The manager is created when the host starts (before any
/// window exists), while the web view is created later by the shell's window pipeline, so the
/// two are joined together lazily through this holder.
/// </summary>
internal sealed class TaruiBlazorHybridState
{
    private readonly object _gate = new();

    public TaruiBlazorWebViewManager? Manager { get; private set; }

    public ITaruiWebView? WebView { get; private set; }

    public void SetManager(TaruiBlazorWebViewManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        lock (_gate)
        {
            if (Manager is not null)
            {
                throw new InvalidOperationException("The Tarui Blazor Hybrid manager is already initialized.");
            }

            Manager = manager;
        }
    }

    public void AttachWebView(ITaruiWebView webView)
    {
        ArgumentNullException.ThrowIfNull(webView);
        lock (_gate)
        {
            WebView = webView;
            webView.HybridMessageReceived += OnWebViewHybridMessage;
        }
    }

    public void DetachWebView(ITaruiWebView webView)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(WebView, webView))
            {
                return;
            }

            webView.HybridMessageReceived -= OnWebViewHybridMessage;
            WebView = null;
        }
    }

    private void OnWebViewHybridMessage(object? sender, TaruiWebMessage message)
    {
        var manager = Manager;
        if (manager is null)
        {
            return;
        }

        // The web view raises events on a worker thread; WebViewManager marshals onto its own
        // dispatcher. Source is read from the web view so WebViewManager can enforce that messages
        // originate from the application origin (defense against remote pages reaching the bridge).
        var source = sender is ITaruiWebView webView ? webView.Source : null;
        manager.OnWebViewMessage(source, message.Message);
    }
}
