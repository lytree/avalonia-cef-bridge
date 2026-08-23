using Avalonia.Controls;

namespace Tarui.Shell;

/// <summary>
/// The visual surface that presents a <see cref="WebviewSession"/> inside a window. It is a pure display
/// shell (a single-cell layout that hosts the session's web view control) and holds no IPC, event or
/// policy logic, so a window can swap, add or remove surfaces without touching the session lifecycle.
/// </summary>
public sealed class WebviewPresenter : Border
{
    /// <summary>The session whose surface this control presents; null after disposal of the window host.</summary>
    public WebviewSession? Session { get; }

    public WebviewPresenter(WebviewSession session)
    {
        Session = session;
        Child = session.WebView.Control;
    }
}