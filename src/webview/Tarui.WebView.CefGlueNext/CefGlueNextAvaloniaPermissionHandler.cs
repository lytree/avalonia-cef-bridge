using Tarui.WebView.Abstractions;
using Xilium.CefGlue;

namespace Tarui.WebView.CefGlueNext;

/// <summary>
/// Bridges CEF's permission callbacks to the host-neutral <see cref="ITaruiWebViewPermissions"/>
/// contract. The owner raises the request synchronously; the decision is applied to the CEF callback
/// before returning. <see cref="TaruiWebViewPermissionResponse.Default"/> returns false so CEF keeps
/// its native handling instead of the host pretending to have answered.
/// </summary>
internal sealed class CefGlueNextAvaloniaPermissionHandler(CefGlueNextAvaloniaWebView owner)
    : CefPermissionHandler
{
    protected override bool OnRequestMediaAccessPermission(
        CefBrowser browser,
        CefFrame frame,
        string requestingOrigin,
        CefMediaAccessPermissionTypes requestedPermissions,
        CefMediaAccessCallback callback)
    {
        var args = new TaruiWebViewPermissionRequestedEventArgs(
            TaruiWebViewPermissionKind.Media,
            requestingOrigin);
        owner.RaisePermissionRequested(args);

        switch (args.Response)
        {
            case TaruiWebViewPermissionResponse.Allow:
                // CEF requires the allowed mask to match the requested one for getUserMedia.
                callback.Continue(requestedPermissions);
                return true;
            case TaruiWebViewPermissionResponse.Deny:
                callback.Cancel();
                return true;
            default:
                return false;
        }
    }

    protected override bool OnShowPermissionPrompt(
        CefBrowser browser,
        ulong promptId,
        string requestingOrigin,
        CefPermissionRequestTypes requestedPermissions,
        CefPermissionPromptCallback callback)
    {
        var args = new TaruiWebViewPermissionRequestedEventArgs(
            Map(requestedPermissions),
            requestingOrigin);
        owner.RaisePermissionRequested(args);

        switch (args.Response)
        {
            case TaruiWebViewPermissionResponse.Allow:
                callback.Continue(CefPermissionRequestResult.Accept);
                return true;
            case TaruiWebViewPermissionResponse.Deny:
                callback.Continue(CefPermissionRequestResult.Deny);
                return true;
            default:
                return false;
        }
    }

    internal static TaruiWebViewPermissionKind Map(CefPermissionRequestTypes requested)
    {
        var known = CefPermissionRequestTypes.None;
        var mapped = TaruiWebViewPermissionKind.None;

        void MapFlag(CefPermissionRequestTypes flag, TaruiWebViewPermissionKind kind)
        {
            if (!requested.HasFlag(flag))
            {
                return;
            }

            known |= flag;
            mapped |= kind;
        }

        MapFlag(CefPermissionRequestTypes.ArSession, TaruiWebViewPermissionKind.Media);
        MapFlag(CefPermissionRequestTypes.CameraPanTiltZoom, TaruiWebViewPermissionKind.Media);
        MapFlag(CefPermissionRequestTypes.CameraStream, TaruiWebViewPermissionKind.Media);
        MapFlag(CefPermissionRequestTypes.CapturedSurfaceControl, TaruiWebViewPermissionKind.Media);
        MapFlag(CefPermissionRequestTypes.MicStream, TaruiWebViewPermissionKind.Media);
        MapFlag(CefPermissionRequestTypes.Clipboard, TaruiWebViewPermissionKind.ClipboardRead);
        MapFlag(CefPermissionRequestTypes.Geolocation, TaruiWebViewPermissionKind.Geolocation);
        MapFlag(CefPermissionRequestTypes.LocalFonts, TaruiWebViewPermissionKind.LocalFonts);
        MapFlag(CefPermissionRequestTypes.MidiSysex, TaruiWebViewPermissionKind.Midi);
        MapFlag(CefPermissionRequestTypes.Notifications, TaruiWebViewPermissionKind.Notifications);
        MapFlag(CefPermissionRequestTypes.PointerLock, TaruiWebViewPermissionKind.PointerLock);
        MapFlag(CefPermissionRequestTypes.WindowManagement, TaruiWebViewPermissionKind.WindowManagement);

        // Any family we have no dedicated flag for still reaches the host as "Other" instead of
        // being silently dropped from the request.
        if ((requested & ~known) != CefPermissionRequestTypes.None)
        {
            mapped |= TaruiWebViewPermissionKind.Other;
        }

        return mapped == TaruiWebViewPermissionKind.None
            ? TaruiWebViewPermissionKind.Other
            : mapped;
    }
}
