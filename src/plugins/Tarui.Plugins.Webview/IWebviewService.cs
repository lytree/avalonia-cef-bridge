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
}