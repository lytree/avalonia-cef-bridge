using Avalonia.Controls;

namespace Tarui.WebView.Abstractions;

public sealed record TaruiWebViewOptions(Uri InitialSource);

public sealed record TaruiWebMessage(string Message);

public interface ITaruiWebView : IDisposable
{
    Control Control { get; }

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

    ValueTask<string?> ExecuteScriptAsync(
        string script,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the current draggable region set and returns the previous set.</summary>
    IReadOnlyList<DraggableRegion> SetDragRegions(IReadOnlyList<DraggableRegion> regions);
}

public interface ITaruiWebViewFactory
{
    ITaruiWebView Create(TaruiWebViewOptions options);
}
