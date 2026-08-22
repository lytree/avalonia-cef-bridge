using System.Diagnostics;
using Tarui.WebView.Abstractions;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace Tarui.WebView.CefGlueNext;

/// <summary>
/// Raises the typed <see cref="ITaruiWebView"/> navigation events for the underlying CEF browser and
/// resolves the host's decision before the load commits. External navigation is handed to the OS
/// default handler instead of navigating the embedded web view.
/// </summary>
internal sealed class CefNavigationRequestHandler : RequestHandler
{
    private readonly CefGlueNextWebView _owner;

    public CefNavigationRequestHandler(CefGlueNextWebView owner)
    {
        _owner = owner;
    }

    protected override bool OnBeforeBrowse(
        CefBrowser browser,
        CefFrame frame,
        CefRequest request,
        bool userGesture,
        bool isRedirect)
    {
        var decision = RaiseNavigationSafe(request.Url, frame.IsMain);
        return Resolve(decision, request.Url);
    }

    protected override bool OnOpenUrlFromTab(
        CefBrowser browser,
        CefFrame frame,
        string targetUrl,
        CefWindowOpenDisposition targetDisposition,
        bool userGesture)
    {
        // target=_blank and equivalent dispositions must never drive a new embedded web view.
        var decision = RaiseNavigationSafe(targetUrl, frame.IsMain);
        return Resolve(decision, targetUrl);
    }

    private TaruiWebViewNavigationAction RaiseNavigationSafe(string url, bool isMainFrame)
    {
        try
        {
            return _owner.RaiseNavigation(new Uri(url, UriKind.Absolute), isMainFrame);
        }
        catch (WebViewRequestDeniedException)
        {
            // A policy denial must never escape into a CEF native callback; cancel the navigation.
            return TaruiWebViewNavigationAction.Deny;
        }
    }

    private static bool Resolve(TaruiWebViewNavigationAction action, string url) => action switch
    {
        TaruiWebViewNavigationAction.Allow => false,
        TaruiWebViewNavigationAction.External => OpenExternal(url),
        _ => true,
    };

    private static bool OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // An unlaunchable external URL is still cancelled in the web view, never loaded in place.
        }

        return true;
    }
}

/// <summary>
/// Raises the typed <see cref="ITaruiWebView"/> download event and starts or cancels the CEF download
/// based on the host's decision. The host resolves the decision before any file output begins.
/// </summary>
internal sealed class CefDownloadHandler : DownloadHandler
{
    private readonly CefGlueNextWebView _owner;

    public CefDownloadHandler(CefGlueNextWebView owner)
    {
        _owner = owner;
    }

    protected override bool OnBeforeDownload(
        CefBrowser browser,
        CefDownloadItem downloadItem,
        string suggestedName,
        CefBeforeDownloadCallback callback)
    {
        var allowed = RaiseDownloadSafe(downloadItem.Url, suggestedName) == TaruiWebViewDownloadAction.Allow;
        if (!allowed)
        {
            // Return false to cancel the download using Alloy-style default handling.
            return false;
        }

        // Hand the path choice to the OS save dialog; null lets the browser pick a default path.
        callback.Continue(null, showDialog: true);
        return true;
    }

    private TaruiWebViewDownloadAction RaiseDownloadSafe(string url, string? suggestedName)
    {
        try
        {
            return _owner.RaiseDownload(url, suggestedName);
        }
        catch (WebViewRequestDeniedException)
        {
            // A policy denial must never escape into a CEF native callback; cancel the download.
            return TaruiWebViewDownloadAction.Deny;
        }
    }
}

/// <summary>
/// Maps CEF drag events to the typed <see cref="ITaruiWebView"/> file drop and draggable region events.
/// <c>OnDraggableRegionsChanged</c> is the renderer-driven source for <c>-webkit-app-region</c> CSS.
/// </summary>
internal sealed class CefDragHandler : DragHandler
{
    private readonly CefGlueNextWebView _owner;

    public CefDragHandler(CefGlueNextWebView owner)
    {
        _owner = owner;
    }

    protected override bool OnDragEnter(CefBrowser browser, CefDragData dragData, CefDragOperationsMask mask)
    {
        // Drop coordinates are not exposed by CefDragHandler; the window-level drop adapter (real-machine
        // acceptance) supplies the control-relative position.
        if (dragData.IsFile)
        {
            return !_owner.RaiseFileDropEntered(dragData.GetFilePaths(), dragData.FragmentText, 0, 0);
        }

        if (dragData.IsFragment)
        {
            return !_owner.RaiseFileDropEntered([], dragData.FragmentText, 0, 0);
        }

        return true;
    }

    protected override void OnDraggableRegionsChanged(CefBrowser browser, CefFrame frame, CefDraggableRegion[] regions)
    {
        var regionsList = new DraggableRegion[regions.Length];
        for (var i = 0; i < regions.Length; i++)
        {
            var bounds = regions[i].Bounds;
            // CEF reports 0,0-relative CSS pixels; keep integers but widen to double for the abstraction.
            regionsList[i] = new DraggableRegion(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                regions[i].Draggable
                    ? DraggableRegionKind.Drag
                    : DraggableRegionKind.NoDrag);
        }

        _owner.RaiseDragRegionsUpdated(regionsList);
    }
}