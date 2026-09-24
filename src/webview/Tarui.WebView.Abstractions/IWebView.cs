namespace Tarui.WebView.Abstractions;

public sealed record TaruiWebViewOptions(Uri InitialSource);

public sealed record TaruiWebMessage(string Message);

public interface ITaruiWebView : IDisposable
{
    Uri? Source { get; }

    event EventHandler<TaruiWebMessage>? MessageReceived;

    /// <summary>
    /// Raised when the page sends a message over the hybrid (in-process Blazor) channel — the
    /// private "__taruiHybrid" process message that backs <c>window.external.sendMessage</c> for
    /// <c>blazor.webview.js</c>. Kept separate from <see cref="MessageReceived"/> so the Tarui IPC
    /// bridge and the in-process Blazor renderer never observe each other's frames.
    /// </summary>
    event EventHandler<TaruiWebMessage>? HybridMessageReceived;

    event EventHandler<TaruiWebViewFileDropEventArgs>? FileDropEntered;

    event EventHandler<TaruiWebViewFileDropLeftEventArgs>? FileDropLeft;

    event EventHandler<TaruiWebViewFileDropEventArgs>? FileDropped;

    /// <summary>Raised before a download starts; the host sets the decision before any file output.</summary>
    event EventHandler<TaruiWebViewDownloadEventArgs>? DownloadRequested;

    /// <summary>Raised before a navigation commits; the host sets the decision before the load.</summary>
    event EventHandler<TaruiWebViewNavigationEventArgs>? NavigationRequested;

    /// <summary>Raised when the renderer publishes draggable region rectangles.</summary>
    event EventHandler<TaruiWebViewDragRegionEventArgs>? DragRegionsUpdated;

    void Navigate(Uri source);

    /// <summary>
    /// Opens or closes the browser's developer tools for this web view. Opening attaches to the
    /// underlying browser's devtools window; closing detaches an already-open instance. A web view
    /// whose browser has not been initialized yet no-ops until the surface is ready.
    /// </summary>
    void SetDevTools(bool open);

    /// <summary>
    /// Evaluates <paramref name="script"/> in the renderer's main frame. The current implementation
    /// does not return a value because the bundled CEF fork's <c>ExecuteJavaScript</c> surface is
    /// fire-and-forget; callers should treat this as a one-way injection. A future revision can swap
    /// the underlying call for <c>CefFrame.EvaluateScriptAsync</c> with a V8-context callback to
    /// materialize a return value without changing this contract.
    /// </summary>
    ValueTask ExecuteScriptAsync(
        string script,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the current draggable region set and returns the previous set.</summary>
    IReadOnlyList<DraggableRegion> SetDragRegions(IReadOnlyList<DraggableRegion> regions);
}

public interface ITaruiWebViewFactory
{
    ITaruiWebView Create(TaruiWebViewOptions options);
}
