namespace Tarui.WebView.Abstractions;

/// <summary>How the host resolves a pending navigation request.</summary>
public enum TaruiWebViewNavigationAction
{
    /// <summary>Allow the navigation to proceed inside the web view.</summary>
    Allow,
    /// <summary>Block the navigation.</summary>
    Deny,
    /// <summary>Hand the navigation to the OS default handler.</summary>
    External,
}

/// <summary>How the host resolves a pending download request.</summary>
public enum TaruiWebViewDownloadAction
{
    Allow,
    Deny,
}

/// <summary>
/// Raised on drag-enter and drop. <c>Paths</c> are absolute, deduplicated file paths; <c>Text</c> is an
/// optional text fragment; <c>X</c>/<c>Y</c> are control-relative logical pixels. The host may set
/// <see cref="Accepted"/> to reject a drop without delivering paths to an un-authorized window.
/// </summary>
public sealed class TaruiWebViewFileDropEventArgs : EventArgs
{
    public TaruiWebViewFileDropEventArgs(string[] paths, string? text, double x, double y)
    {
        Paths = paths;
        Text = text;
        X = x;
        Y = y;
    }

    public string[] Paths { get; }

    public string? Text { get; }

    public double X { get; }

    public double Y { get; }

    public bool Accepted { get; set; } = true;
}

/// <summary>Raised when a file drag leaves the web view without a drop.</summary>
public sealed class TaruiWebViewFileDropLeftEventArgs : EventArgs
{
    public static TaruiWebViewFileDropLeftEventArgs Instance { get; } = new();

    private TaruiWebViewFileDropLeftEventArgs()
    {
    }
}

/// <summary>
/// Raised when the browser proposes a download. The host (via policy and capability authorization)
/// sets <see cref="Decision"/> before the adapter starts any file output. Defaults to
/// <see cref="TaruiWebViewDownloadAction.Deny"/>.
/// </summary>
public sealed class TaruiWebViewDownloadEventArgs : EventArgs
{
    public TaruiWebViewDownloadEventArgs(string url, string? suggestedFilename)
    {
        Url = url;
        SuggestedFilename = suggestedFilename;
    }

    public string Url { get; }

    public string? SuggestedFilename { get; }

    public TaruiWebViewDownloadAction Decision { get; set; } = TaruiWebViewDownloadAction.Deny;
}

/// <summary>
/// Raised when the browser requests a navigation. The host sets <see cref="Decision"/> before the
/// adapter commits the load. When it stays null the adapter applies its default (deny).
/// </summary>
public sealed class TaruiWebViewNavigationEventArgs : EventArgs
{
    public TaruiWebViewNavigationEventArgs(Uri url, bool isMainFrame)
    {
        Url = url;
        IsMainFrame = isMainFrame;
    }

    public Uri Url { get; }

    public bool IsMainFrame { get; }

    public TaruiWebViewNavigationAction? Decision { get; set; }
}

/// <summary>Raised when the renderer publishes the draggable region rectangles for the page.</summary>
public sealed class TaruiWebViewDragRegionEventArgs : EventArgs
{
    public TaruiWebViewDragRegionEventArgs(IReadOnlyList<DraggableRegion> regions)
    {
        Regions = regions;
    }

    public IReadOnlyList<DraggableRegion> Regions { get; }
}