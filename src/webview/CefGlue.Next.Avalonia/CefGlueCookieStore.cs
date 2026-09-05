using System.Net;
using Xilium.CefGlue;

namespace CefGlue.Next.Avalonia;

/// <summary>
/// Component-level access to the embedded CEF global cookie store. Operations resolve the global cookie manager
/// on demand and complete asynchronously through CEF callbacks, so no cookies are touched unless the runtime is
/// actually available. Every operation returns <c>null</c> / an empty result rather than throwing when the runtime
/// is not initialized, letting a host degrade honestly instead of fabricating cookies.
/// </summary>
public static class CefGlueCookieStore
{
    /// <summary>Lists the cookies that apply to <paramref name="url"/>. Returns null when the store is unavailable.</summary>
    public static Task<IEnumerable<Cookie>?> GetCookiesAsync(string url, bool includeHttpOnly)
    {
        var manager = ResolveManager();
        if (manager is null)
        {
            return Task.FromResult<IEnumerable<Cookie>?>(null);
        }

        var visitor = new CookieVisitor();
        if (!manager.VisitUrlCookies(url, includeHttpOnly, visitor))
        {
            return Task.FromResult<IEnumerable<Cookie>?>(null);
        }

        return visitor.Task;
    }

    /// <summary>Sets a cookie for <paramref name="url"/>. Returns null when the store is unavailable.</summary>
    public static Task<bool?> SetCookieAsync(string url, Cookie cookie)
    {
        var manager = ResolveManager();
        if (manager is null)
        {
            return Task.FromResult<bool?>(null);
        }

        var callback = new SetCookieCallback();
        if (!manager.SetCookie(url, ToCefCookie(cookie), callback))
        {
            return Task.FromResult<bool?>(null);
        }

        return callback.Task;
    }

    /// <summary>Deletes matching cookies. Returns null when the store is unavailable.</summary>
    public static Task<int?> DeleteCookiesAsync(string url, string name)
    {
        var manager = ResolveManager();
        if (manager is null)
        {
            return Task.FromResult<int?>(null);
        }

        var callback = new DeleteCookiesCallback();
        if (!manager.DeleteCookies(url, name, callback))
        {
            return Task.FromResult<int?>(null);
        }

        return callback.Task;
    }

    /// <summary>Flushes buffered cookie writes to disk. Returns null when the store is unavailable.</summary>
    public static Task<bool?> FlushStoreAsync()
    {
        var manager = ResolveManager();
        if (manager is null)
        {
            return Task.FromResult<bool?>(null);
        }

        var callback = new CompletionCallback();
        if (!manager.FlushStore(callback))
        {
            return Task.FromResult<bool?>(null);
        }

        return callback.Task;
    }

    private static CefCookieManager? ResolveManager()
    {
        // Synchronous resolve: the global manager exists once CEF is initialized. We avoid the async GetGlobal
        // callback because it never completes when CEF is not running, which would hang a caller.
        try
        {
            return CefCookieManager.GetGlobal(null);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static CefCookie ToCefCookie(Cookie cookie)
    {
        CefBaseTime? expires = null;
        if (cookie.Expires > DateTime.MinValue)
        {
            var utc = cookie.Expires.ToUniversalTime();
            if (CefBaseTime.FromUtcExploded(new CefTime(utc), out var baseTime))
            {
                expires = baseTime;
            }
        }

        return new CefCookie
        {
            Name = cookie.Name,
            Value = cookie.Value,
            Domain = cookie.Domain ?? string.Empty,
            Path = cookie.Path ?? string.Empty,
            Secure = cookie.Secure,
            HttpOnly = cookie.HttpOnly,
            Expires = expires,
        };
    }

    private static Cookie FromCefCookie(CefCookie cookie)
    {
        DateTime expires = default;
        if (cookie.Expires is { } expiry && expiry.UtcExplode(out var exploded))
        {
            expires = exploded.ToDateTime();
        }

        return new Cookie(cookie.Name ?? string.Empty, cookie.Value ?? string.Empty, cookie.Domain, cookie.Path)
        {
            Secure = cookie.Secure,
            HttpOnly = cookie.HttpOnly,
            Expires = expires,
        };
    }

    private sealed class CookieVisitor : CefCookieVisitor
    {
        private readonly List<Cookie> _cookies = [];
        private readonly TaskCompletionSource<IEnumerable<Cookie>?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IEnumerable<Cookie>?> Task => _completion.Task;

        protected override bool Visit(CefCookie cookie, int count, int total, out bool delete)
        {
            _cookies.Add(FromCefCookie(cookie));
            delete = false;
            if (count + 1 >= total)
            {
                _completion.TrySetResult(_cookies);
            }

            return true;
        }
    }

    private sealed class SetCookieCallback : CefSetCookieCallback
    {
        private readonly TaskCompletionSource<bool?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool?> Task => _completion.Task;

        protected override void OnComplete(bool success) => _completion.TrySetResult(success);
    }

    private sealed class DeleteCookiesCallback : CefDeleteCookiesCallback
    {
        private readonly TaskCompletionSource<int?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int?> Task => _completion.Task;

        protected override void OnComplete(int numDeleted) => _completion.TrySetResult(numDeleted);
    }

    private sealed class CompletionCallback : CefCompletionCallback
    {
        private readonly TaskCompletionSource<bool?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool?> Task => _completion.Task;

        protected override void OnComplete() => _completion.TrySetResult(true);
    }
}