namespace Tarui.Shell;

/// <summary>
/// Parses and validates a custom-protocol URL against the registered scheme set. Deep-link URLs are
/// carried only as data into the renderer (via <c>get-current</c> and <c>deeplink://&lt;scheme&gt;</c>
/// events); the native side never treats them as commands. Validation rejects empty/oversized
/// inputs, control characters (including CR/LF that could poison logs), and any scheme the app did
/// not register.
/// </summary>
internal static class DeepLinkUri
{
    /// <summary>Upper bound on accepted URL length, mirrored from the contract's threat model.</summary>
    public const int MaxLength = 2048;

    public static bool IsValidScheme(string scheme)
    {
        if (string.IsNullOrEmpty(scheme))
        {
            return false;
        }

        if (!char.IsAsciiLetter(scheme[0]))
        {
            return false;
        }

        foreach (var c in scheme)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the scheme of a valid registered deep-link URL, or <see langword="null"/> when the
    /// value is malformed, oversized, contains control characters, or uses an unregistered scheme.
    /// </summary>
    public static string? TryExtractScheme(string? value, IReadOnlySet<string> schemes)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return null;
        }

        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return null;
            }
        }

        var separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return null;
        }

        var scheme = value[..separator];
        return schemes.Contains(scheme) ? scheme : null;
    }
}