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
        : BuildUnixSocketPath();

    /// <summary>
    /// Composes the Unix socket path and folds it deterministically when it would exceed the
    /// <see cref="MaxUnixSocketPathLength"/> limit of <c>sockaddr_un.sun_path</c>. The fold must be
    /// a pure function of the identity and the environment: the primary binds the path a forwarded
    /// secondary later connects to. SHA-256 is used only as a non-cryptographic mixing function,
    /// for the same reason as <see cref="EndpointSuffix"/>.
    /// </summary>
    private string BuildUnixSocketPath()
    {
        var appId = SanitizedApplicationId;
        var root = ResolveUnixSocketRoot(appId);
        var fileName = $"tarui-{appId}-{SanitizedChannelName}-{EndpointSuffix()}.sock";
        var path = Path.Combine(root, fileName);
        if (path.Length <= MaxUnixSocketPathLength)
        {
            return path;
        }

        // 第一步：文件名折叠为 16 位十六进制哈希，保留 app 子目录。应用间隔离仍由文件名区分。
        var mixed = SHA256.HashData(Encoding.UTF8.GetBytes($"{appId}|{SanitizedChannelName}|unix-socket"));
        var shortName = $"tarui-{Convert.ToHexString(mixed).AsSpan(0, 16).ToString().ToLowerInvariant()}.sock";
        path = Path.Combine(root, shortName);
        if (path.Length <= MaxUnixSocketPathLength)
        {
            return path;
        }

        // 第二步：根目录本身过长（macOS /var/folders 临时目录约 47 字符 + 64 字符 app 子目录）
        // 时退到无 app 子目录的共享根；不同应用的隔离完全由文件名哈希保证。
        return Path.Combine(ResolveUnixSocketRoot(null), shortName);
    }

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

    private static string ResolveUnixSocketRoot(string? sanitizedApplicationId)
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            return sanitizedApplicationId is null
                ? Path.Combine(runtime, "tarui")
                : Path.Combine(runtime, "tarui", sanitizedApplicationId);
        }

        // Fallback: a per-user, per-app subfolder. On macOS this resolves to /var/folders/.../T/,
        // which is already per-user; the OS enforces directory isolation between users.
        return sanitizedApplicationId is null
            ? Path.Combine(Path.GetTempPath(), "tarui")
            : Path.Combine(Path.GetTempPath(), "tarui", sanitizedApplicationId);
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

    /// <summary>Unix domain socket 路径上限：sockaddr_un.sun_path 的 108 字节（.NET 含终止 NUL 一并计入）。</summary>
    internal const int MaxUnixSocketPathLength = 108;

    /// <summary>Stable cross-platform identifier derived from <see cref="CultureInfo.InvariantCulture"/>.</summary>
    internal string CultureInvariantTag => $"{SanitizedApplicationId}-{SanitizedChannelName}".ToLowerInvariant();
}
