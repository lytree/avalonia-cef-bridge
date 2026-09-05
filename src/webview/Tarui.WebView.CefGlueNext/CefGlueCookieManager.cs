using CefGlue.Next.Avalonia;
using Tarui.Contracts;
using Tarui.WebView.Abstractions;
using NetCookie = System.Net.Cookie;

namespace Tarui.WebView.CefGlueNext;

/// <summary>
/// <see cref="IWebViewCookieManager"/> backed by the embedded CEF global cookie store via the
/// <see cref="CefGlueCookieStore"/> component. Every operation reports <c>Supported = false</c> with a descriptive
/// error when the browser layer is not initialized, rather than fabricating an empty cookie list.
/// </summary>
public sealed class CefGlueCookieManager : IWebViewCookieManager
{
    public async ValueTask<CookieListResult> ListAsync(CookieListOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cookies = await CefGlueCookieStore.GetCookiesAsync(options.Url, options.IncludeHttpOnly);
        cancellationToken.ThrowIfCancellationRequested();
        if (cookies is null)
        {
            return new CookieListResult(false, [], "The embedded browser cookie store is unavailable.");
        }

        return new CookieListResult(true, cookies.Select(ToContract).ToArray());
    }

    public async ValueTask<CookieSetResult> SetAsync(CookieSetOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var success = await CefGlueCookieStore.SetCookieAsync(options.Url, ToNetCookie(options.Cookie));
        cancellationToken.ThrowIfCancellationRequested();
        if (success is null)
        {
            return new CookieSetResult(false, "The embedded browser cookie store is unavailable.");
        }

        return new CookieSetResult(success.Value, success.Value ? null : "The embedded browser rejected the cookie.");
    }

    public async ValueTask<CookieDeleteResult> RemoveAsync(CookieDeleteOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleted = await CefGlueCookieStore.DeleteCookiesAsync(options.Url ?? string.Empty, options.Name ?? string.Empty);
        cancellationToken.ThrowIfCancellationRequested();
        if (deleted is null)
        {
            return new CookieDeleteResult(false, "The embedded browser cookie store is unavailable.");
        }

        return new CookieDeleteResult(true);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await CefGlueCookieStore.FlushStoreAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static Cookie ToContract(NetCookie cookie)
    {
        long? expires = null;
        if (cookie.Expires > DateTime.MinValue)
        {
            expires = new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeMilliseconds();
        }

        return new Cookie(
            cookie.Name ?? string.Empty,
            cookie.Value ?? string.Empty,
            NullIfDefault(cookie.Domain),
            NullIfDefault(cookie.Path),
            cookie.Secure,
            cookie.HttpOnly,
            expires);
    }

    private static NetCookie ToNetCookie(Cookie cookie) => new(
        cookie.Name,
        cookie.Value,
        cookie.Domain ?? string.Empty,
        cookie.Path ?? "/")
    {
        Secure = cookie.Secure,
        HttpOnly = cookie.HttpOnly,
        Expires = cookie.Expires is { } milliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime
            : DateTime.MinValue,
    };

    private static string? NullIfDefault(string? value) => string.IsNullOrEmpty(value) ? null : value;
}