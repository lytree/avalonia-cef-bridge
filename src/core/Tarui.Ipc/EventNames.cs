namespace Tarui.Ipc;

/// <summary>
/// Defines event name namespaces for the Tarui shell. Web-originated custom events must use the
/// <c>user://</c> namespace; all other prefixes are reserved for events that native code emits
/// and are never reachable from the Web renderer.
/// </summary>
public static class EventNames
{
    /// <summary>The only namespace Web code may emit events under.</summary>
    public const string UserNamespace = "user://";

    /// <summary>
    /// Native event prefixes reserved by the shell. These carry system or window data (focus,
    /// geometry, theme, second-instance arguments, path/notification payloads) and must never be
    /// forged by the Web renderer. Delivery of these events is additionally gated by per-window
    /// receive authorization.
    /// </summary>
    private static readonly string[] ReservedPrefixes =
    [
        "app://",
        "window://",
        "webview://",
        "shell://",
        "menu://",
        "tray://",
        "notification://",
        "global-shortcut://",
        "fs://",
        "updater://",
        "log://",
        "deeplink://"
    ];

    /// <summary>Returns <see langword="true"/> when the event belongs to the <c>user://</c> namespace.</summary>
    public static bool IsUserEvent(string eventName)
        => !string.IsNullOrEmpty(eventName) &&
           eventName.StartsWith(UserNamespace, StringComparison.Ordinal);

    /// <summary>Returns <see langword="true"/> when the event uses a reserved native prefix.</summary>
    public static bool IsReserved(string eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return false;
        }

        foreach (var prefix in ReservedPrefixes)
        {
            if (eventName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Enforces that a Web-originated emit only targets the <c>user://</c> namespace, preventing
    /// the renderer from forging native events.
    /// </summary>
    public static void ValidateWebEmit(string eventName)
    {
        if (!IsUserEvent(eventName))
        {
            throw new EventNamespaceDeniedException(eventName);
        }
    }
}