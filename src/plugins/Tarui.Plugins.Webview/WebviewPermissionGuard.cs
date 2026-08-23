using Tarui.Ipc;

namespace Tarui.Plugins.Webview;

/// <summary>
/// Enforces cross-webview authorization. Addressing a web view other than the caller's own requires
/// the related <c>-other-webview</c> permission; the plain current-webview permission never implicitly
/// covers another surface.
/// </summary>
public static class WebviewPermissionGuard
{
    /// <summary>Returns the <c>-other-webview</c> variant of <paramref name="permission"/>.</summary>
    public static string OtherWebviewPermission(string permission) => permission + "-other-webview";

    /// <summary>
    /// Allows <paramref name="targetWebviewLabel"/> when it equals the caller's own web view label.
    /// Otherwise requires the <c>-other-webview</c> variant of <paramref name="permission"/>.
    /// </summary>
    public static void EnsureOwnOrOtherWebview(
        CommandContext context,
        string targetWebviewLabel,
        string permission)
    {
        if (string.Equals(targetWebviewLabel, context.WebViewLabel, StringComparison.Ordinal))
        {
            return;
        }

        var other = OtherWebviewPermission(permission);
        if (!context.Capabilities.Allows(other))
        {
            throw new PermissionDeniedException(other);
        }
    }
}