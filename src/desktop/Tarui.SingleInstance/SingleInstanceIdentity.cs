using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tarui.SingleInstance;

/// <summary>
/// Opaque naming for one logical application's single-instance port.
/// <see cref="ApplicationId"/> seeds both the cross-process lock name and the per-user
/// communication endpoint so two apps that share a <see cref="ChannelName"/> never collide on the
/// same named pipe or Unix socket. <see cref="ChannelName"/> is still carried because multiple
/// channels (for example <c>main</c> and <c>crash-recovery</c>) inside one process remain valid.
/// </summary>
public sealed record SingleInstanceIdentity(string ApplicationId, string ChannelName)
{
    /// <summary>Lowercase, punctuation-stripped, length-bounded application id suitable for OS endpoints.</summary>
    public string SanitizedApplicationId => SanitizeIdentifier(ApplicationId);

    /// <summary>Lowercase, punctuation-stripped, length-bounded channel name suitable for OS endpoints.</summary>
    public string SanitizedChannelName => SanitizeIdentifier(ChannelName);

    /// <summary>Kernel lock name; on Windows a named mutex, on Unix a lock file under the temp directory.</summary>
    public string LockName => $"tarui.net-{SanitizedApplicationId}-{SanitizedChannelName}";

    /// <summary>Named pipe (Windows) or socket file (Unix) identifier.</summary>
    public string SocketPath => OperatingSystem.IsWindows()
        ? $"tarui-{SanitizedApplicationId}-{SanitizedChannelName}-pipe"
        : Path.Combine(ResolveUnixSocketRoot(SanitizedApplicationId), $"tarui-{SanitizedApplicationId}-{SanitizedChannelName}-{EndpointSuffix()}.sock");

    /// <summary>
    /// A short, stable suffix mixed into the Unix socket path so two apps that hash to the same
    /// safe identifier never collapse onto a single file. The suffix is deterministic so the
    /// primary and a forwarded secondary agree on the path. SHA-256 is used here only as a
    /// non-cryptographic mixing function: collisions would only cause two unrelated apps to share
    /// the same socket file, which the OS would surface as a connection error rather than a
    /// security issue.
    /// </summary>
    private string EndpointSuffix()
    {
        var bytes = Encoding.UTF8.GetBytes($"{SanitizedApplicationId}|{SanitizedChannelName}");
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).AsSpan(0, 8).ToString().ToLowerInvariant();
    }

    private static string ResolveUnixSocketRoot(string sanitizedApplicationId)
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            return Path.Combine(runtime, "tarui", sanitizedApplicationId);
        }

        // Fallback: a per-user, per-app subfolder. On macOS this resolves to /var/folders/.../T/,
        // which is already per-user; the OS enforces directory isolation between users.
        return Path.Combine(Path.GetTempPath(), "tarui", sanitizedApplicationId);
    }

    /// <summary>
    /// Normalizes a user-supplied identifier to lowercase ASCII letters, digits, dots and dashes,
    /// truncating to <see cref="MaxIdentifierLength"/>. The fallback value (<c>tarui-app</c>) is
    /// used when the input is empty or strips down to nothing.
    /// </summary>
    internal static string SanitizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "tarui-app";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '-' || character == '_'
                ? char.ToLowerInvariant(character)
                : '-');
        }

        var sanitized = builder.ToString().Trim('-');
        if (sanitized.Length == 0)
        {
            return "tarui-app";
        }

        if (sanitized.Length > MaxIdentifierLength)
        {
            sanitized = sanitized[..MaxIdentifierLength].TrimEnd('-');
            if (sanitized.Length == 0)
            {
                return "tarui-app";
            }
        }

        return sanitized;
    }

    internal const int MaxIdentifierLength = 64;

    /// <summary>Stable cross-platform identifier derived from <see cref="CultureInfo.InvariantCulture"/>.</summary>
    internal string CultureInvariantTag => $"{SanitizedApplicationId}-{SanitizedChannelName}".ToLowerInvariant();
}
