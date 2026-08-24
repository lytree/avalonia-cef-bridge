using Tarui.Contracts;

namespace Tarui.Ipc;

/// <summary>
/// Shared glob-style matcher used by file and store scope authorizers. The matcher picks
/// <see cref="StringComparison.OrdinalIgnoreCase"/> on Windows so deny entries cannot be bypassed
/// with a different casing of the same path; other platforms stay ordinal so a Linux desktop user
/// can deliberately distinguish <c>Notes.txt</c> from <c>notes.txt</c>.
/// </summary>
public static class FileScopeMatcher
{
    /// <summary>
    /// Whether <paramref name="path"/> under <paramref name="baseName"/> is allowed by the
    /// <paramref name="allow"/> scopes and not denied by the <paramref name="deny"/> scopes.
    /// Deny always wins.
    /// </summary>
    public static bool MatchesScope(
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        string? baseName,
        string? path)
    {
        foreach (var scope in deny)
        {
            if (MatchesOne(scope, baseName, path))
            {
                return false;
            }
        }

        if (allow.Count == 0)
        {
            return true;
        }

        foreach (var scope in allow)
        {
            if (MatchesOne(scope, baseName, path))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a single <paramref name="pattern"/> (a glob using forward slashes and the
    /// <c>*</c> / <c>**</c> wildcards) covers <paramref name="candidate"/>. Comparisons honour
    /// the host OS filename casing rules. A trailing <c>*</c> on the final pattern segment is
    /// treated as "match the rest of the candidate path" so a base-level pattern like
    /// <c>/var/data*</c> covers any file under <c>/var/data</c>.
    /// </summary>
    public static bool MatchGlob(string pattern, string candidate)
    {
        var patternSegments = pattern.Replace('\\', '/').Split('/', StringSplitOptions.None);
        var candidateSegments = candidate.Replace('\\', '/').Split('/', StringSplitOptions.None);
        return MatchSegments(patternSegments.AsSpan(), candidateSegments.AsSpan());
    }

    private static bool MatchesOne(PathScope scope, string? baseName, string? requestPath)
    {
        var relative = requestPath ?? string.Empty;
        if (!string.IsNullOrEmpty(scope.Base) &&
            !StringComparer.Ordinal.Equals(scope.Base, baseName))
        {
            return false;
        }

        if (string.IsNullOrEmpty(scope.Path))
        {
            return true;
        }

        return MatchGlob(scope.Path, relative);
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

            // A trailing '*' on the final pattern segment matches the remainder of the candidate
            // path; this lets base-level patterns like '/var/data*' cover everything beneath.
            if (pattern.Length == 1 &&
                pattern[0].EndsWith('*') &&
                pattern[0].IndexOf('*') == pattern[0].Length - 1)
            {
                var prefix = pattern[0][..^1];
                var comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return candidate.Length == 0
                    ? prefix.Length == 0
                    : candidate[0].StartsWith(prefix, comparison) ||
                      MatchSegmentAcrossRemainder(pattern[0], candidate, comparison);
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

    /// <summary>
    /// Confirms a final pattern segment containing exactly one trailing <c>*</c> covers every
    /// remaining candidate segment by gluing them back together and applying the per-segment
    /// matcher semantics. This is what lets <c>Temp*</c> match <c>Temp/icon.ico</c>.
    /// </summary>
    private static bool MatchSegmentAcrossRemainder(string patternSegment, ReadOnlySpan<string> candidate, StringComparison comparison)
    {
        var joined = string.Join('/', candidate.ToArray());
        var starIndex = patternSegment.IndexOf('*');
        var prefix = starIndex >= 0 ? patternSegment[..starIndex] : patternSegment;
        var suffix = starIndex >= 0 ? patternSegment[(starIndex + 1)..] : string.Empty;
        if (suffix.Length > 0)
        {
            return false;
        }

        return joined.StartsWith(prefix, comparison);
    }

    private static bool MatchSegment(string patternSegment, string candidateSegment)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (patternSegment == "*")
        {
            return candidateSegment.Length > 0;
        }

        var starIndex = patternSegment.IndexOf('*');
        if (starIndex < 0)
        {
            return string.Equals(patternSegment, candidateSegment, comparison);
        }

        var prefix = patternSegment[..starIndex];
        var suffix = patternSegment[(starIndex + 1)..];
        if (suffix.Contains('*'))
        {
            return string.Equals(patternSegment, candidateSegment, comparison);
        }

        return candidateSegment.StartsWith(prefix, comparison) &&
               candidateSegment.EndsWith(suffix, comparison) &&
               candidateSegment.Length >= prefix.Length + suffix.Length;
    }
}
