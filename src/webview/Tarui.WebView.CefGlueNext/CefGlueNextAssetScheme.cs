using Tarui.WebView.Abstractions;

namespace Tarui.WebView.CefGlueNext;

/// <summary>
/// Adapts the host-owned <see cref="ITaruiAssetResolver"/> to the CEF resource-provider contract.
/// The resolver already enforces its root allow-list, so this adapter only translates the response.
/// </summary>
internal sealed class CefGlueNextAvaloniaAssetResourceProvider(ITaruiAssetResolver resolver)
    : ICefGlueNextAvaloniaResourceProvider
{
    public CefGlueNextAvaloniaResourceResponse Resolve(CefGlueNextAvaloniaResourceRequest request)
    {
        var asset = resolver.Resolve(request.Url, request.Method);
        return new CefGlueNextAvaloniaResourceResponse(
            asset.Status,
            asset.StatusText,
            asset.MimeType,
            asset.CacheControl,
            asset.ResponseLength,
            Content: [],
            asset.Headers,
            asset.ContentStream);
    }
}
