using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Threading;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Webview;
using Tarui.WebView.Abstractions;
using Tarui.WebView.Avalonia;

namespace Tarui.Shell;

/// <summary>
/// Shell-backed <see cref="IWebviewService"/>. A web view is resolved from the live web view session
/// owned by the window with the matching label (web view and window share one label while windows host
/// a single surface). Navigations run on the UI thread and are confined to the application origin's
/// accepted schemes — HTTP(S) and, when local assets are served, the portless custom app scheme.
/// </summary>
public sealed class AvaloniaWebviewService(WindowRegistry registry, TaruiAppOrigin appOrigin) : IWebviewService
{
    // The wrapper script is handed straight to the JS engine via ExecuteScript, never embedded into
    // HTML, so the relaxed encoder is the correct choice — the default encoder would escape safe
    // characters such as '+' and distort evaluated source code.
    private static readonly JsonSerializerOptions EvalScriptEncoding = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

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

    public ValueTask<Unit> SetDevToolsAsync(string webviewLabel, bool open, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var entry = registry.Get(webviewLabel);
            var session = entry.Webview
                ?? throw new InvalidOperationException($"No web view session is mounted for '{webviewLabel}'.");
            session.WebView.SetDevTools(open);
        }).GetTask().GetAwaiter().GetResult();
        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> SetZoomAsync(string webviewLabel, double factor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var webView = ResolveWebView(webviewLabel);
        webView.SetZoom(factor);
        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> PrintAsync(string webviewLabel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var webView = ResolveWebView(webviewLabel);
        webView.Print();
        return ValueTask.FromResult(new Unit());
    }

    public async ValueTask<Unit> EvalAsync(string webviewLabel, string script, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        var webView = ResolveWebView(webviewLabel);
        await webView.ExecuteScriptAsync(script, cancellationToken);
        return new Unit();
    }

    public async ValueTask<Unit> EvalWithCallbackAsync(
        string webviewLabel,
        string script,
        string? channelId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        var webView = ResolveWebView(webviewLabel);
        await webView.ExecuteScriptAsync(BuildEvalCallbackScript(script, channelId), cancellationToken);
        return new Unit();
    }

    /// <summary>
    /// Builds the wrapper that evaluates a user script and posts its completion back into the shell
    /// through the fixed <c>invokeCSharpAction</c> bridge. The shell then forwards the outcome to the
    /// caller's channel as a <see cref="WebviewEvalFrame"/>, so the front-end gets a real completion
    /// callback even though the bundled CEF fork's <c>ExecuteJavaScript</c> cannot return values
    /// natively. The script is embedded as a JSON string literal and evaluated with
    /// <c>window.eval</c>, which preserves completion-value semantics (the last expression's value)
    /// and resolves returned promises before the completion frame is delivered.
    /// </summary>
    internal static string BuildEvalCallbackScript(string script, string? channelId)
    {
        var id = string.IsNullOrWhiteSpace(channelId) ? "eval-cb-unbound" : channelId;
        var encodedScript = JsonSerializer.Serialize(script, EvalScriptEncoding);
        return $$"""
            (function() {
              var deliver = function(ok, payload) {
                try {
                  var bridge = window.invokeCSharpAction;
                  if (!bridge) { return; }
                  bridge(JSON.stringify({
                    protocol: 1,
                    id: 'eval-cb-completion',
                    command: 'plugin:webview|eval-callback-complete',
                    payload: { id: '{{id}}', ok: ok, value: ok ? payload : null, error: ok ? null : payload },
                    windowLabel: 'main',
                    webViewLabel: 'main'
                  }));
                } catch (_) { /* the bridge is gone; the completion is dropped */ }
              };
              Promise.resolve()
                .then(function() { return window.eval({{encodedScript}}); })
                .then(
                  function(value) {
                    var encoded;
                    try {
                      encoded = JSON.stringify(value === undefined ? null : value);
                    } catch (error) {
                      deliver(false, 'eval result is not JSON-serializable: ' + String((error && error.message) || error));
                      return;
                    }
                    deliver(true, encoded);
                  },
                  function(error) { deliver(false, String((error && error.message) || error)); });
            })();
            """;
    }

    private ITaruiAvaloniaWebView ResolveWebView(string webviewLabel)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            var entry = registry.Get(webviewLabel);
            var session = entry.Webview
                ?? throw new InvalidOperationException($"No web view session is mounted for '{webviewLabel}'.");
            return session.WebView;
        }).GetTask().GetAwaiter().GetResult();
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