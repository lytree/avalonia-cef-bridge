using Microsoft.AspNetCore.Components.WebView;
using Tarui.WebView.CefGlueNext;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// Serves the Blazor application over the <c>tarui://</c> custom scheme by delegating every
/// request to <see cref="WebViewManager.TryGetResponseContent"/>. That keeps asset resolution
/// identical to the official BlazorWebView hosts: app files come from the content root, static
/// web assets (including <c>_framework/blazor.webview.js</c>) come from the package-managed
/// manifest, and unmatched main-frame paths may fall back to the host page for SPA deep links.
/// </summary>
internal sealed class TaruiBlazorSchemeContentProvider : ICefGlueNextAvaloniaResourceProvider
{
    private readonly TaruiBlazorHybridState _state;
    private readonly string _contentSecurityPolicy;

    public TaruiBlazorSchemeContentProvider(TaruiBlazorHybridState state, string? contentSecurityPolicy)
    {
        _state = state;
        _contentSecurityPolicy = contentSecurityPolicy ?? string.Empty;
    }

    public CefGlueNextAvaloniaResourceResponse Resolve(CefGlueNextAvaloniaResourceRequest request)
    {
        var manager = _state.Manager;
        if (manager is null)
        {
            return Error(503, "Blazor renderer is not initialized yet.");
        }

        var allowFallbackOnHostPage = request.IsMainFrameResource;
        if (!manager.TryServeContent(
                request.Url,
                allowFallbackOnHostPage,
                out var statusCode,
                out var statusMessage,
                out var content,
                out var headers))
        {
            // The URL is outside the application base URI; the scheme handler only receives
            // requests for the registered scheme, so treat this as a not-found response.
            return Error(404, "Not Found");
        }

        var merged = AddProtectionHeaders(headers);
        var contentType = merged.TryGetValue("Content-Type", out var mimeType) ? mimeType : "application/octet-stream";
        merged.TryGetValue("Cache-Control", out var cacheControl);

        return new CefGlueNextAvaloniaResourceResponse(
            Status: statusCode,
            StatusText: statusMessage,
            MimeType: contentType,
            CacheControl: cacheControl ?? "no-cache",
            ResponseLength: content.CanSeek ? content.Length : 0,
            Content: [],
            Headers: merged,
            ContentStream: content);
    }

    private Dictionary<string, string> AddProtectionHeaders(IDictionary<string, string> headers)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in headers)
        {
            merged[key] = value;
        }

        merged["X-Content-Type-Options"] = "nosniff";
        if (!string.IsNullOrWhiteSpace(_contentSecurityPolicy))
        {
            merged["Content-Security-Policy"] = _contentSecurityPolicy;
        }

        return merged;
    }

    private static CefGlueNextAvaloniaResourceResponse Error(int status, string statusText)
    {
        var body = System.Text.Encoding.UTF8.GetBytes($"{status} {statusText}");
        return new CefGlueNextAvaloniaResourceResponse(
            Status: status,
            StatusText: statusText,
            MimeType: "text/plain; charset=utf-8",
            CacheControl: "no-store",
            ResponseLength: body.LongLength,
            Content: body);
    }
}
