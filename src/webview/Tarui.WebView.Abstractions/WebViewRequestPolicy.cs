namespace Tarui.WebView.Abstractions;

/// <summary>The kind of request the host must decide.</summary>
public enum WebViewRequestKind
{
    /// <summary>A main-frame or popup navigation target.</summary>
    Navigation,
    /// <summary>A download; only actually started after the policy allows it.</summary>
    Download,
}

/// <summary>The host's decision for a navigation or download request.</summary>
public enum WebViewRequestDecision
{
    /// <summary>Proceed inside the web view (navigation) or start the download.</summary>
    Allow,
    /// <summary>Block the request and do nothing.</summary>
    Deny,
    /// <summary>Hand a navigation to the OS default handler; never used for downloads.</summary>
    External,
}

/// <summary>Why a request was denied. Distinguishes malformed/malicious input from plain policy.</summary>
public enum WebViewDenialReason
{
    /// <summary>The URL is relative, empty, or otherwise not an absolute absolute http(s) URL.</summary>
    MalformedUrl,
    /// <summary>The URL uses a scheme the web view must never load (javascript:, data:, file:, ...).</summary>
    UnsupportedScheme,
    /// <summary>The URL or its port/path contains a control character.</summary>
    ControlCharacter,
    /// <summary>The URL is well-formed but not covered by any allow or external rule.</summary>
    NotAllowed,
}

/// <summary>
/// Denial signal produced by <see cref="WebViewRequestPolicy"/>. A request that yields
/// <see cref="WebViewRequestDecision.Deny"/> carries one of these so the shell can route the
/// <c>webview://download-requested</c> / <c>webview://navigation-requested</c> events and report a
/// stable error to the front end without leaking the "why" over an unauthorized channel.
/// </summary>
public sealed class WebViewRequestDeniedException(WebViewDenialReason reason)
    : Exception($"Web view request denied ({reason}).")
{
    public WebViewDenialReason Reason { get; } = reason;
}

/// <summary>
/// Pure, dependency-free configuration for <see cref="WebViewRequestPolicy"/>. Patterns are URL globs
/// (<c>https://app.example/**</c>, <c>http://localhost:*/*</c>). For navigation, rules are evaluated
/// deny-from-malformed first, then external, then allow, with an implicit deny fallback. Downloads only
/// ever allow or deny.
/// </summary>
public sealed record WebViewPolicyOptions(
    IReadOnlyList<string> AllowedNavigationPatterns,
    IReadOnlyList<string> ExternalNavigationPatterns,
    IReadOnlyList<string> AllowedDownloadHostPatterns,
    WebViewRequestDecision DefaultDownloadDecision = WebViewRequestDecision.Deny)
{
    public static WebViewPolicyOptions None { get; } = new(
        AllowedNavigationPatterns: [],
        ExternalNavigationPatterns: [],
        AllowedDownloadHostPatterns: [],
        DefaultDownloadDecision: WebViewRequestDecision.Deny);
}

/// <summary>
/// The pure strategy engine that turns a navigation or download URL into a host decision. It never
/// touches CEF, so it can be unit-tested in isolation and reused by the shell route and the CefGlue
/// adapter. Malicious inputs (relative URLs, unsafe schemes, control characters) are always denied.
/// </summary>
public sealed class WebViewRequestPolicy
{
    private static readonly string[] AllowedSchemes = ["http", "https"];

    private readonly WebViewPolicyOptions _options;

    public WebViewRequestPolicy(WebViewPolicyOptions options)
    {
        _options = options ?? WebViewPolicyOptions.None;
    }

    /// <summary>Decides a navigation target. Result is never <see cref="WebViewRequestDecision.Allow"/> for
    /// a malformed or malicious URL.</summary>
    public WebViewRequestDecision DecideNavigation(Uri url)
    {
        Validate(url);

        var external = _options.ExternalNavigationPatterns;
        foreach (var pattern in external)
        {
            if (MatchGlob(pattern, url))
            {
                return WebViewRequestDecision.External;
            }
        }

        var allowed = _options.AllowedNavigationPatterns;
        foreach (var pattern in allowed)
        {
            if (MatchGlob(pattern, url))
            {
                return WebViewRequestDecision.Allow;
            }
        }

        return WebViewRequestDecision.Deny;
    }

    /// <summary>Decides whether a download may start. Malformed or malicious URLs are always denied.</summary>
    public WebViewRequestDecision DecideDownload(Uri url)
    {
        Validate(url);

        foreach (var pattern in _options.AllowedDownloadHostPatterns)
        {
            if (MatchHostGlob(pattern, url))
            {
                return WebViewRequestDecision.Allow;
            }
        }

        return _options.DefaultDownloadDecision;
    }

    /// <summary>
    /// Validates that <paramref name="url"/> is an absolute http(s) URL with no control characters.
    /// Throws <see cref="WebViewRequestDeniedException"/> otherwise. A URL whose scheme is not
    /// http(s) is rejected even if a configured allow pattern would otherwise match.
    /// </summary>
    private static void Validate(Uri url)
    {
        if (!url.IsAbsoluteUri)
        {
            throw new WebViewRequestDeniedException(WebViewDenialReason.MalformedUrl);
        }

        var scheme = url.Scheme;
        if (!Array.Exists(AllowedSchemes, s => s == scheme))
        {
            throw new WebViewRequestDeniedException(WebViewDenialReason.UnsupportedScheme);
        }

        var rendered = url.ToString();
        if (rendered.Any(char.IsControl))
        {
            throw new WebViewRequestDeniedException(WebViewDenialReason.ControlCharacter);
        }
    }

    /// <summary>
    /// Matches a URL glob (for example <c>https://example.com/**</c>, <c>http://localhost:*/*</c>)
    /// against an absolute URL using '/' segment semantics with <c>*</c> (one segment) and
    /// <c>**</c> (zero or more segments). Scheme and host are matched exactly (case-insensitive).
    /// </summary>
    private static bool MatchGlob(string pattern, Uri url)
    {
        var normalized = $"{url.Scheme}://{url.Authority}{url.AbsolutePath}";
        return MatchSegments(
            Split(pattern.Replace('\\', '/')),
            Split(normalized));
    }

    /// <summary>Matches a host-only glob (for example <c>*.cdn.example</c>, <c>localhost</c>) against the URL host.</summary>
    private static bool MatchHostGlob(string pattern, Uri url)
    {
        return MatchSegments(
            Split(pattern.Replace('\\', '/')),
            Split(url.Host));
    }

    private static string[] Split(string value)
    {
        var trimmed = value.TrimEnd('/');
        return trimmed.Split('/', StringSplitOptions.None);
    }

    private static bool MatchSegments(ReadOnlySpan<string> pattern, ReadOnlySpan<string> candidate)
    {
        while (pattern.Length > 0)
        {
            if (pattern[0] == "**")
            {
                var remainingPattern = pattern[1..];
                for (var start = 0; start <= candidate.Length; start++)
                {
                    if (MatchSegments(remainingPattern, candidate[start..]))
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

            if (!MatchSegment(pattern[0], candidate[0]))
            {
                return false;
            }

            pattern = pattern[1..];
            candidate = candidate[1..];
        }

        return candidate.Length == 0;
    }

    private static bool MatchSegment(string patternSegment, string candidateSegment)
    {
        if (patternSegment == "*")
        {
            return candidateSegment.Length > 0;
        }

        var starIndex = patternSegment.IndexOf('*');
        if (starIndex < 0)
        {
            return string.Equals(patternSegment, candidateSegment, StringComparison.OrdinalIgnoreCase);
        }

        var prefix = patternSegment[..starIndex];
        var suffix = patternSegment[(starIndex + 1)..];
        if (suffix.Contains('*'))
        {
            return string.Equals(patternSegment, candidateSegment, StringComparison.OrdinalIgnoreCase);
        }

        return candidateSegment.Length >= prefix.Length + suffix.Length
            && candidateSegment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && candidateSegment.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}