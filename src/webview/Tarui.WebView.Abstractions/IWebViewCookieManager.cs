using Tarui.Contracts;

namespace Tarui.WebView.Abstractions;

/// <summary>
/// Host-side cookie store abstraction backed by the embedded browser. The cookie plugin consumes this
/// interface rather than reaching into CEF directly so the same commands run on any host that supplies a
/// manager. Hosts with no persistent cookie store (or no browser integration yet) should return a result
/// with <c>Supported = false</c> and a descriptive <see cref="CookieListResult.Error"/> instead of fabricating
/// an empty list, so callers degrade honestly.
/// </summary>
public interface IWebViewCookieManager
{
    /// <summary>Lists cookies that apply to <paramref name="options"/>.<see cref="CookieListOptions.Url"/>.</summary>
    ValueTask<CookieListResult> ListAsync(
        CookieListOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Sets a cookie for <paramref name="options"/>.<see cref="CookieSetOptions.Url"/>.</summary>
    ValueTask<CookieSetResult> SetAsync(
        CookieSetOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a named cookie, all cookies for a URL, or every cookie per <paramref name="options"/>.</summary>
    ValueTask<CookieDeleteResult> RemoveAsync(
        CookieDeleteOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Flushes any buffered cookie writes to persistent storage, if the host caches them.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);
}