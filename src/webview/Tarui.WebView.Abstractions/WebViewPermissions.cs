namespace Tarui.WebView.Abstractions;

/// <summary>
/// The browser permission families a web view can intercept, normalized across backends. The flags
/// mirror the families exposed by wry's permission-handler API; a backend that cannot observe a
/// family simply never reports it.
/// </summary>
[Flags]
public enum TaruiWebViewPermissionKind
{
    None = 0,

    /// <summary>Camera / microphone capture (device or desktop).</summary>
    Media = 1 << 0,

    Geolocation = 1 << 1,

    Notifications = 1 << 2,

    /// <summary>Web MIDI (system-exclusive) access.</summary>
    Midi = 1 << 3,

    ClipboardRead = 1 << 4,

    LocalFonts = 1 << 5,

    PointerLock = 1 << 6,

    WindowManagement = 1 << 7,

    /// <summary>Any permission family the backend reports that has no dedicated flag yet.</summary>
    Other = 1 << 8,
}

/// <summary>How the host answers a permission request.</summary>
public enum TaruiWebViewPermissionResponse
{
    /// <summary>Let the browser backend apply its default handling (no host decision).</summary>
    Default,

    Allow,

    Deny,
}

/// <summary>
/// Raised before the browser resolves a permission request. The host sets <see cref="Response"/>
/// synchronously; the handler applies it before the request continues.
/// </summary>
public sealed class TaruiWebViewPermissionRequestedEventArgs : EventArgs
{
    public TaruiWebViewPermissionRequestedEventArgs(
        TaruiWebViewPermissionKind requested,
        string requestingOrigin)
    {
        Requested = requested;
        RequestingOrigin = requestingOrigin;
    }

    /// <summary>The permission families the page requested; a request may combine several.</summary>
    public TaruiWebViewPermissionKind Requested { get; }

    /// <summary>The URL origin the page reports as the requester.</summary>
    public string RequestingOrigin { get; }

    /// <summary>The host decision. Defaults to <see cref="TaruiWebViewPermissionResponse.Default"/>.</summary>
    public TaruiWebViewPermissionResponse Response { get; set; } = TaruiWebViewPermissionResponse.Default;
}

/// <summary>
/// Optional web view capability: a backend that can intercept permission requests implements this
/// interface in addition to <see cref="ITaruiWebView"/>. Hosts must treat a backend without it as
/// "permissions are left to the browser default" instead of claiming a decision was made.
/// </summary>
public interface ITaruiWebViewPermissions
{
    event EventHandler<TaruiWebViewPermissionRequestedEventArgs>? PermissionRequested;
}
