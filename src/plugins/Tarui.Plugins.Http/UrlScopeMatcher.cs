using Tarui.Contracts;
using HttpRequestOptions = Tarui.Contracts.HttpRequestOptions;

namespace Tarui.Plugins.Http;

/// <summary>
/// Authorizes absolute URLs against structured permission allow/deny <see cref="PathScope"/>s, where each
/// <see cref="PathScope.Path"/> holds a URL glob of the form <c>scheme://host[:port][/path]</c>. Deny always
/// wins; in contrast to the file matcher, an empty allow list is a deny-all by default so HTTP never opens
/// up silently when a permission is granted without an explicit URL scope.
/// </summary>
public static class UrlScopeMatcher
{
    /// <summary>
    /// Whether <paramref name="url"/> is allowed: denied by any <paramref name="deny"/> scope, or allowed by
    /// at least one <paramref name="allow"/> scope. Returns <see langword="false"/> when allow is empty.
    /// </summary>
    public static bool AllowsUrl(IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var request))
        {
            return false;
        }

        foreach (var scope in deny)
        {
            if (MatchesOne(scope.Path, request))
            {
                return false;
            }
        }

        foreach (var scope in allow)
        {
            if (MatchesOne(scope.Path, request))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesOne(string? pattern, Uri request)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        // Split "scheme://authority/path".
        var schemeSeparator = pattern.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator <= 0)
        {
            return false;
        }

        var scheme = pattern[..schemeSeparator];
        if (!string.Equals(scheme, request.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = pattern[(schemeSeparator + 3)..];
        var pathStart = rest.IndexOf('/');
        var authority = pathStart < 0 ? rest : rest[..pathStart];
        var patternPath = pathStart < 0 ? "/" : rest[pathStart..];

        var host = authority;
        var port = string.Empty;
        var colon = authority.LastIndexOf(':');
        if (colon > 0)
        {
            host = authority[..colon];
            port = authority[(colon + 1)..];
        }

        if (!MatchesHost(host, request.Host) || !MatchesPort(port, request))
        {
            return false;
        }

        var requestPath = string.IsNullOrEmpty(request.AbsolutePath) ? "/" : request.AbsolutePath;
        return MatchUrlPath(patternPath, requestPath);
    }

    /// <summary>
    /// Matches an HTTP path glob where <c>*</c> covers a single non-empty segment and <c>**</c> spans any
    /// number of segments. Path segments compare case-insensitively so a deny scope cannot be bypassed with a
    /// different casing of the same path.
    /// </summary>
    private static bool MatchUrlPath(string pattern, string candidate)
    {
        var patternSegments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var candidateSegments = candidate.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return MatchSegments(patternSegments, candidateSegments);
    }

    private static bool MatchSegments(ReadOnlySpan<string> pattern, ReadOnlySpan<string> candidate)
    {
        while (pattern.Length > 0)
        {
            var segment = pattern[0];
            if (segment == "**")
            {
                var leftover = pattern[1..];
                for (var i = 0; i <= candidate.Length; i++)
                {
                    if (MatchSegments(leftover, candidate[i..]))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (candidate.Length == 0)
            {
                return false;
            }

            var current = candidate[0];
            if (segment != "*" && !string.Equals(segment, current, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            pattern = pattern[1..];
            candidate = candidate[1..];
        }

        return candidate.Length == 0;
    }

    private static bool MatchesHost(string patternHost, string requestHost)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        if (patternHost == "*")
        {
            return true;
        }

        if (patternHost.StartsWith("*.", StringComparison.Ordinal))
        {
            // 前缀通配只匹配任意子域，不匹配裸域。
            var suffix = patternHost[1..];
            return requestHost.EndsWith(suffix, comparison);
        }

        return string.Equals(patternHost, requestHost, comparison);
    }

    private static bool MatchesPort(string patternPort, Uri request)
    {
        var schemeDefault = request.Scheme == Uri.UriSchemeHttps ? 443 : 80;
        if (patternPort.Length == 0)
        {
            // 未显式声明端口时，仅允许 scheme 默认端口，防止策略意外放开其它端口的同名服务。
            return request.Port == schemeDefault;
        }

        return int.TryParse(patternPort, out var requested) && requested == request.Port;
    }
}

/// <summary>Authorizes <see cref="HttpRequestOptions"/> URL scopes for the fetch command.</summary>
public static class HttpScopeAuthorizer
{
    public static bool AllowsUrl(HttpRequestOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        UrlScopeMatcher.AllowsUrl(allow, deny, options.Url);
}