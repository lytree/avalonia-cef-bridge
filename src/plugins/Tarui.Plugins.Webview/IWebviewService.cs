using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Webview;

/// <summary>
/// Cross-plugin contract for driving web views independently of their host windows. Web views are
/// separated from windows so a surface can navigate, report state or be enumerated without owning —
/// or being owned by — the native window that frames it.
/// </summary>
public interface IWebviewService
{
    ValueTask<Unit> NavigateAsync(string webviewLabel, string url, CancellationToken cancellationToken);

    ValueTask<WebviewStateInfo> GetStateAsync(string webviewLabel, CancellationToken cancellationToken);

    ValueTask<Unit> SetDevToolsAsync(string webviewLabel, bool open, CancellationToken cancellationToken);

    ValueTask<Unit> SetZoomAsync(string webviewLabel, double factor, CancellationToken cancellationToken);

    ValueTask<Unit> PrintAsync(string webviewLabel, CancellationToken cancellationToken);

    ValueTask<Unit> EvalAsync(string webviewLabel, string script, CancellationToken cancellationToken);

    ValueTask<Unit> EvalWithCallbackAsync(
        string webviewLabel,
        string script,
        string? channelId,
        CancellationToken cancellationToken);

    ValueTask<string[]> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates an additional web view inside an existing window. The surface inherits the host window's
    /// capability profile; null <see cref="WebviewCreateOptions.Bounds"/> makes it fill the window.
    /// </summary>
    ValueTask<WebviewStateInfo> CreateAsync(WebviewCreateOptions options, CancellationToken cancellationToken);

    /// <summary>Repositions or resizes a web view surface; null bounds restore window fill.</summary>
    ValueTask<Unit> SetBoundsAsync(string webviewLabel, WebviewBounds? bounds, CancellationToken cancellationToken);

    /// <summary>Destroys a web view, releasing its surface and session.</summary>
    ValueTask<Unit> DestroyAsync(string webviewLabel, CancellationToken cancellationToken);

    /// <summary>
    /// Reports the identity of the browser runtime backing every web view (name, engine version,
    /// resource mode and application origin). This is runtime-wide, not per-web-view.
    /// </summary>
    ValueTask<WebviewRuntimeInfo> GetRuntimeInfoAsync(CancellationToken cancellationToken);
}