using Avalonia.Controls;
using Tarui.Contracts;

namespace Tarui.Shell;

/// <summary>
/// The visual surface that presents a <see cref="WebviewSession"/> inside a window. It is a pure display
/// shell that hosts the session's web view control and holds no IPC, event or policy logic, so a window
/// can swap, add or remove surfaces without touching the session lifecycle.
/// </summary>
public sealed class WebviewPresenter : Border
{
    /// <summary>The session whose surface this control presents; null after disposal of the window host.</summary>
    public WebviewSession? Session { get; }

    /// <summary>The surface bounds within the host window; null fills the window.</summary>
    public WebviewBounds? SurfaceBounds { get; private set; }

    public WebviewPresenter(WebviewSession session)
    {
        Session = session;
        Child = session.WebView.Control;
    }

    /// <summary>Positions and sizes the surface within its host window, or restores window fill when null.</summary>
    public void SetBounds(WebviewBounds? bounds)
    {
        SurfaceBounds = bounds;
        InvalidateArrange();
    }
}