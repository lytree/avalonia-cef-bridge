using Tarui.Ipc;

namespace Tarui.Plugins.Window;

/// <summary>
/// Enforces cross-window authorization. Operating on a window other than the caller's own window
/// requires the related <c>-other-window</c> permission; the plain current-window permission never
/// implicitly covers another window.
/// </summary>
public static class WindowPermissionGuard
{
    /// <summary>Returns the <c>-other-window</c> variant of <paramref name="permission"/>.</summary>
    public static string OtherWindowPermission(string permission) => permission + "-other-window";

    /// <summary>
    /// Allows <paramref name="targetLabel"/> when it equals the caller's own window label. Otherwise
    /// requires the <c>-other-window</c> variant of <paramref name="permission"/>.
    /// </summary>
    public static void EnsureOwnOrOtherWindow(
        CommandContext context,
        string targetLabel,
        string permission)
    {
        if (string.Equals(targetLabel, context.WindowLabel, StringComparison.Ordinal))
        {
            return;
        }

        var other = OtherWindowPermission(permission);
        if (!context.Capabilities.Allows(other))
        {
            throw new PermissionDeniedException(other);
        }
    }
}