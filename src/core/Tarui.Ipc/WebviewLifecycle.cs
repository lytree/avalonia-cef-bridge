namespace Tarui.Ipc;

/// <summary>
/// Observes web view surface teardown so services that own web-view-scoped native resources can
/// release them once the surface is gone — either the whole window closed or the surface was
/// destroyed explicitly. The Shell notifies every explicitly registered observer; nothing is
/// discovered by scanning.
/// </summary>
public interface IWebviewLifecycleObserver
{
    /// <summary>Raised after the surface <paramref name="webviewLabel"/> hosted by <paramref name="windowLabel"/> is torn down.</summary>
    void OnWebviewClosed(string windowLabel, string webviewLabel);
}
