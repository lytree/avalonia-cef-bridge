using System.Text;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// The Tarui implementation of the official <see cref="WebViewManager"/> contract: it drives the
/// CEF-backed <see cref="ITaruiWebView"/> instead of WebView2 or a MAUI handler. Interop frames
/// travel over the private "__taruiHybrid" web view channel (JS to host) and one-way script
/// injection into <c>window.__taruiHybridReceive</c> (host to JS), matching the
/// <c>window.external.sendMessage/receiveMessage</c> surface that <c>blazor.webview.js</c> uses.
/// </summary>
internal sealed class TaruiBlazorWebViewManager : WebViewManager
{
    private readonly TaruiBlazorHybridState _state;
    private readonly Uri _appBaseUri;

    public TaruiBlazorWebViewManager(
        IServiceProvider provider,
        TaruiBlazorHybridState state,
        Uri appBaseUri,
        IFileProvider fileProvider,
        string hostPageRelativePath)
        : base(
            provider,
            Microsoft.AspNetCore.Components.Dispatcher.CreateDefault(),
            appBaseUri,
            fileProvider,
            new JSComponentConfigurationStore(),
            hostPageRelativePath)
    {
        _state = state;
        _appBaseUri = appBaseUri;
    }

    /// <summary>Forwards a hybrid-channel frame from the web view into the Blazor IPC pipeline.</summary>
    internal void OnWebViewMessage(Uri? source, string message)
    {
        // Drop frames without a known source: WebViewManager ignores messages outside the app
        // base URI, and a null source means the web view had not reported a URL yet.
        if (source is null)
        {
            return;
        }

        MessageReceived(source, message);
    }

    /// <summary>Internal bridge onto the protected static-content resolver for the scheme handler.</summary>
    internal bool TryServeContent(
        string requestUri,
        bool allowFallbackOnHostPage,
        out int statusCode,
        out string statusMessage,
        out Stream content,
        out System.Collections.Generic.IDictionary<string, string> headers)
        => TryGetResponseContent(requestUri, allowFallbackOnHostPage, out statusCode, out statusMessage, out content, out headers);

    protected override void NavigateCore(Uri absoluteUri)
    {
        var webView = _state.WebView;
        if (webView is null)
        {
            // No window has been opened yet; the shell navigates the initial URL itself. Blazor
            // only triggers NavigateCore for programmatic full-page navigations.
            return;
        }

        webView.Navigate(absoluteUri);
    }

    protected override void SendMessage(string message)
    {
        var webView = _state.WebView;
        if (webView is null)
        {
            return;
        }

        // Base64 keeps the frame free of quotes and control characters inside the single-quoted
        // JS string; the shim decodes it and invokes the callback registered via
        // window.external.receiveMessage. Fire-and-forget mirrors the underlying ExecuteJavaScript
        // surface, which never returns a value.
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(message));
        FireAndForget(webView.ExecuteScriptAsync($"window.__taruiHybridReceive?.('{encoded}')"));
    }

    private static void FireAndForget(ValueTask scriptTask)
    {
        if (scriptTask.IsCompletedSuccessfully)
        {
            // Consume the completed ValueTask exactly once so pooled wrappers are released.
            scriptTask.GetAwaiter().GetResult();
            return;
        }

        _ = AwaitAndIgnoreAsync(scriptTask.AsTask());

        static async Task AwaitAndIgnoreAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // The web view may already be closing when a final interop frame is flushed.
            }
        }
    }
}
