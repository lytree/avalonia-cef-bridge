namespace Tarui.Contracts;

/// <summary>
/// A browser cookie. <see cref="Expires"/> is milliseconds since Unix epoch (null = session cookie / no expiry);
/// <see cref="SameSite"/> is one of <c>unspecified</c>/<c>lax</c>/<c>strict</c>/<c>none</c> when known.
/// </summary>
public sealed record Cookie(
    string Name,
    string Value,
    string? Domain = null,
    string? Path = null,
    bool Secure = false,
    bool HttpOnly = false,
    long? Expires = null,
    string? SameSite = null);

/// <summary>Request for <c>plugin:cookie|list</c>: cookies for <see cref="Url"/> (include HttpOnly).</summary>
public sealed record CookieListOptions(string Url, bool IncludeHttpOnly = true);

/// <summary>Result of listing cookies. <see cref="Supported"/> is false and <see cref="Error"/> set when the
/// platform/browser layer has no cookie store (honest degrade, never a fake empty list).</summary>
public sealed record CookieListResult(bool Supported, Cookie[] Cookies, string? Error = null);

/// <summary>Request for <c>plugin:cookie|set</c>.</summary>
public sealed record CookieSetOptions(string Url, Cookie Cookie);

/// <summary>Result of setting a cookie.</summary>
public sealed record CookieSetResult(bool Succeeded, string? Error = null);

/// <summary>Request for <c>plugin:cookie|remove</c>: delete a named cookie for <see cref="Url"/>, all for the
/// URL when <see cref="Name"/> is null, or every cookie when <see cref="Url"/> is also null.</summary>
public sealed record CookieDeleteOptions(string? Url = null, string? Name = null);

/// <summary>Result of removing cookies.</summary>
public sealed record CookieDeleteResult(bool Succeeded, string? Error = null);