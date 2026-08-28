namespace Tarui.WebView.Abstractions;

public sealed record TaruiWebViewOptions(Uri InitialSource);

public sealed record TaruiWebMessage(string Message);

public interface ITaruiWebView : IDisposable
{
    Uri? Source { get; }

    event EventHandler<TaruiWebMessage>? MessageReceived;

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
