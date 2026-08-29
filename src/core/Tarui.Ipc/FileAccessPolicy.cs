using Microsoft.Extensions.DependencyInjection;

namespace Tarui.Ipc;

/// <summary>Whether a file operation reads from or writes to an authorized directory.</summary>
public enum FileAccessKind
{
    Read,
    Write,
}

/// <summary>Why a requested path was rejected by the file access policy.</summary>
public enum PathDenialReason
{
    /// <summary>The request path is absolute or rooted-relative (for example <c>\x</c>, <c>/x</c>).</summary>
    Rooted,
    /// <summary>The request path is a device path (for example <c>\\?\</c>, <c>\\.\</c>) or a UNC share.</summary>
    DeviceOrUnc,
    /// <summary>The request path contains a control character.</summary>
    ControlCharacter,
    /// <summary>The request path contains an illegal segment (empty, <c>.</c>, <c>..</c>, an invalid filename character, or a drive-letter segment).</summary>
    IllegalSegment,
    /// <summary>The resolved path escapes the authorized base directory.</summary>
    OutsideBase,
    /// <summary>A symbolic link or reparse point in the path resolves outside the authorized base directory.</summary>
    LinkEscape,
    /// <summary>The operation exceeds a per-operation or cumulative byte limit.</summary>
    SizeLimit,
}

/// <summary>
/// Thrown by <see cref="IFileAccessPolicy"/> when a path or byte count is rejected.
/// <see cref="CommandRouter"/> maps this to a stable <c>PATH_DENIED</c> error code.
/// </summary>
public sealed class PathAccessDeniedException(PathDenialReason reason, string message)
    : Exception(message)
{
    public PathDenialReason Reason { get; } = reason;
}

/// <summary>
/// The single, security-authoritative gate for all file capabilities. Plugins must never
/// concatenate user paths themselves; they resolve a base directory and authorize a relative
/// request through this policy, which rejects rooted/device/control/illegal paths, ensures the
/// fully link-resolved target stays inside the base, enforces size limits, and provides atomic
/// durable writes.
/// </summary>
public interface IFileAccessPolicy
{
    /// <summary>
    /// Resolves a symbolic base name (<c>appData</c>, <c>appLocalData</c>, <c>appConfig</c>,
    /// <c>appCache</c>, <c>appLog</c>, <c>temp</c>, <c>resources</c>, ...) to a rooted directory
    /// and reports whether that base is read-only.
    /// </summary>
    bool TryGetBaseDirectory(string baseName, out string directoryPath, out bool isReadOnly);

    /// <summary>
    /// Authorizes <paramref name="requestPath"/> (a relative, user-supplied path) against
    /// <paramref name="baseDirectory"/>. On success returns the normalized absolute path that stays
    /// inside the base directory even after symbolic-link / reparse-point resolution. Throws
    /// <see cref="PathAccessDeniedException"/> otherwise.
    /// </summary>
    string Authorize(FileAccessKind kind, string baseDirectory, string requestPath);

    /// <summary>
    /// Resolves the same symbolic base name as <see cref="TryGetBaseDirectory"/> to a physical
    /// absolute path without requiring the directory to exist. Returns <see langword="null"/>
    /// when the base is unknown. Useful for callers that must compare scopes against the real
    /// physical location before the base directory has been created.
    /// </summary>
    string? ResolveBase(string baseName);

    /// <summary>Whether <paramref name="byteCount"/> is within the per-operation limit for the kind.</summary>
    bool IsWithinOperationLimit(FileAccessKind kind, long byteCount);

    /// <summary>Acquires a slice of the cumulative byte budget; returns <see langword="false"/> when exhausted.</summary>
    bool TryReserveTotalBytes(long byteCount);

    /// <summary>Returns previously reserved bytes to the cumulative budget on failure or rewrite.</summary>
    void ReleaseTotalBytes(long byteCount);

    /// <summary>
    /// Writes <paramref name="content"/> durably using a temporary file plus atomic replace, so a
    /// mid-write failure never corrupts the original file. Enforces per-operation and cumulative
    /// size limits.
    /// </summary>
    Task WriteAllBytesAtomicAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);
}

/// <summary>Configuration for <see cref="FileAccessPolicy"/>.</summary>
public sealed record FileAccessPolicyOptions(
    long MaxReadBytes = 8 * 1024 * 1024,
    long MaxWriteBytes = 8 * 1024 * 1024,
    long MaxTotalBytes = long.MaxValue);

/// <summary>
/// Default <see cref="IFileAccessPolicy"/> implementation. Performs lexical containment checks and
/// then walks the path segment by segment performing symbolic-link / reparse-point aware resolution so
/// the final real target stays inside the authorized base directory. Writes are durable via temporary
/// file plus atomic replace.
/// </summary>
public sealed class FileAccessPolicy : IFileAccessPolicy
{
    private const string AppName = "tarui.net";

    private readonly FileAccessPolicyOptions _options;
    private long _reservedTotalBytes;
    private readonly object _gate = new();

    public FileAccessPolicy(FileAccessPolicyOptions? options = null)
        => _options = options ?? new();

    public bool TryGetBaseDirectory(string baseName, out string directoryPath, out bool isReadOnly)
    {
        isReadOnly = string.Equals(baseName, "resources", StringComparison.OrdinalIgnoreCase);

        var path = ResolveBase(baseName);
        if (path is not null)
        {
            // 应用自有基目录（appData/appLocalData/appConfig/appCache/appLog）首次访问即创建。
            // 否则首启时 store/fs 的读路径会因目录缺失而失败——写路径的 CreateDirectory 排在
            // TryGetBaseDirectory 之后，永远执行不到。用户目录类基目录（home/desktop/...）不创建。
            if (!Directory.Exists(path) && IsAppOwnedBase(baseName))
            {
                Directory.CreateDirectory(path);
            }

            if (Directory.Exists(path))
            {
                directoryPath = path;
                return true;
            }
        }

        directoryPath = string.Empty;
        return false;
    }

    private static bool IsAppOwnedBase(string baseName) => baseName switch
    {
        "appData" or "appLocalData" or "appConfig" or "appCache" or "appLog" => true,
        _ => false,
    };

    /// <inheritdoc />
    public string? ResolveBase(string baseName) => ResolveBaseInternal(baseName);

    public string Authorize(FileAccessKind kind, string baseDirectory, string requestPath)
    {
        // A missing request means "operate on the base root itself".
        if (string.IsNullOrEmpty(requestPath))
        {
            return ResolveWithinBase(baseDirectory, []);
        }

        // 1. Reject device paths and UNC shares up front so they never reach rooted/segment checks.
        if (IsUncOrDevicePath(requestPath))
        {
            throw new PathAccessDeniedException(PathDenialReason.DeviceOrUnc,
                "Device paths and UNC shares are not allowed.");
        }

        // 2. Reject control characters.
        if (requestPath.Any(char.IsControl))
        {
            throw new PathAccessDeniedException(PathDenialReason.ControlCharacter,
                "The path contains a control character.");
        }

        // 3. Reject rooted and drive-relative paths (absolute, \x, /x, C:\x, C:x).
        if (Path.IsPathRooted(requestPath))
        {
            throw new PathAccessDeniedException(PathDenialReason.Rooted,
                "Only relative paths are accepted.");
        }

        // 4. Validate individual segments.
        var segments = requestPath.Split(['\\', '/']);
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                throw new PathAccessDeniedException(PathDenialReason.IllegalSegment,
                    "The path contains an empty segment.");
            }

            if (segment == "." || segment == "..")
            {
                throw new PathAccessDeniedException(PathDenialReason.IllegalSegment,
                    $"The path segment '{segment}' is not allowed.");
            }

            if (segment.Contains(':'))
            {
                throw new PathAccessDeniedException(PathDenialReason.IllegalSegment,
                    "Drive-relative and alternate data stream segments are not allowed.");
            }

            if (segment.IndexOfAny(['"', '<', '>', '|']) >= 0)
            {
                throw new PathAccessDeniedException(PathDenialReason.IllegalSegment,
                    $"The path segment '{segment}' contains an invalid filename character.");
            }
        }

        // 5. Perform a lexical containment check, then a segment-wise link-safe resolution.
        return ResolveWithinBase(baseDirectory, segments);
    }

    public bool IsWithinOperationLimit(FileAccessKind kind, long byteCount)
    {
        var limit = kind == FileAccessKind.Read ? _options.MaxReadBytes : _options.MaxWriteBytes;
        return byteCount >= 0 && byteCount <= limit;
    }

    public bool TryReserveTotalBytes(long byteCount)
    {
        if (byteCount < 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (byteCount > _options.MaxTotalBytes - _reservedTotalBytes)
            {
                return false;
            }

            _reservedTotalBytes += byteCount;
            return true;
        }
    }

    public void ReleaseTotalBytes(long byteCount)
    {
        if (byteCount < 0)
        {
            return;
        }

        lock (_gate)
        {
            _reservedTotalBytes = Math.Max(0, _reservedTotalBytes - byteCount);
        }
    }

    public async Task WriteAllBytesAtomicAsync(
        string targetPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        if (!IsWithinOperationLimit(FileAccessKind.Write, content.Length))
        {
            throw new PathAccessDeniedException(PathDenialReason.SizeLimit,
                "The write exceeds the per-operation size limit.");
        }

        if (!TryReserveTotalBytes(content.Length))
        {
            throw new PathAccessDeniedException(PathDenialReason.SizeLimit,
                "The write exceeds the cumulative size budget.");
        }

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            var fileName = Path.GetFileName(targetPath);
            var tempPath = Path.Combine(
                directory is null or "" ? "." : directory,
                $".{fileName}.{Guid.NewGuid():N}.tmp");

            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            ReleaseTotalBytes(content.Length);
        }
    }

    /// <summary>
    /// Resolves <paramref name="segments"/> under <paramref name="baseDirectory"/>, enforcing both
    /// lexical containment and, at every existing step, symbolic-link / reparse-point resolution so
    /// the final target never escapes the real base root.
    /// </summary>
    private static string ResolveWithinBase(string baseDirectory, string[] segments)
    {
        var baseRoot = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var baseReal = ResolveRealPath(baseRoot);

        var current = baseRoot;
        foreach (var segment in segments)
        {
            current = Path.GetFullPath(Path.Combine(current, segment));
            if (!IsWithinBase(current, baseRoot))
            {
                throw new PathAccessDeniedException(PathDenialReason.OutsideBase,
                    "The resolved path escapes the authorized base directory.");
            }

            var real = ResolveRealPath(current);
            if (!IsWithinBase(real, baseReal))
            {
                throw new PathAccessDeniedException(PathDenialReason.LinkEscape,
                    "A symbolic link or reparse point escapes the authorized base directory.");
            }
        }

        return current;
    }

    private static bool IsWithinBase(string fullPath, string baseFull)
    {
        var baseTrim = baseFull.TrimEnd(Path.DirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var relative = Path.GetRelativePath(baseTrim, fullPath);
        if (relative == "." || relative.Length == 0)
        {
            return true;
        }

        return !Path.IsPathRooted(relative)
            && !relative.Equals("..", comparison)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, comparison);
    }

    private static string ResolveRealPath(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }

        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }

        return path;
    }

    private static bool IsUncOrDevicePath(string path)
    {
        return path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith("\\\\?\\", StringComparison.Ordinal)
            || path.StartsWith("\\.\\", StringComparison.Ordinal);
    }

    private static string? ResolveBaseInternal(string? kind) => kind switch
    {
        "appData" => UnderApp(Environment.SpecialFolder.ApplicationData),
        "appLocalData" => UnderApp(Environment.SpecialFolder.LocalApplicationData),
        "appConfig" => UnderApp(Environment.SpecialFolder.LocalApplicationData, "config"),
        "appCache" => UnderApp(Environment.SpecialFolder.LocalApplicationData, "cache"),
        "appLog" => UnderApp(Environment.SpecialFolder.LocalApplicationData, "logs"),
        "temp" => Path.GetTempPath(),
        "home" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "download" => UnderHome("Downloads"),
        "document" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "video" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "fonts" => Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        "resources" => AppContext.BaseDirectory,
        _ => null,
    };

    private static string? UnderApp(Environment.SpecialFolder folder, string? suffix = null)
    {
        var root = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        return suffix is null ? Path.Combine(root, AppName) : Path.Combine(root, AppName, suffix);
    }

    private static string? UnderHome(string suffix)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? null : Path.Combine(home, suffix);
    }
}

public static class FileAccessPolicyServiceCollectionExtensions
{
    public static IServiceCollection AddFileAccessPolicy(
        this IServiceCollection services,
        FileAccessPolicyOptions? options = null)
        => services.AddSingleton<IFileAccessPolicy>(_ => new FileAccessPolicy(options ?? new()));
}