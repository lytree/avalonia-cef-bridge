namespace Tarui.Contracts;

/// <summary>
/// Common options for every file system command: <see cref="Base"/> names a known base directory
/// (<c>appData</c>, <c>appLocalData</c>, <c>appConfig</c>, <c>appCache</c>, <c>appLog</c>, <c>temp</c>,
/// <c>resources</c>) and <see cref="Path"/> is the user-supplied relative path under that base.
/// The plugin never accepts absolute paths from the web layer; they are validated by
/// <c>IFileAccessPolicy</c> before touching disk.
/// </summary>
public sealed record FsPathOptions(string Base, string? Path = null);

public sealed record FsReadTextResult(string Contents);

public sealed record FsWriteTextOptions(string Base, string? Path = null, string Contents = "");

public sealed record FsReadDirOptions(string Base, string? Path = null, bool Recursive = false);

public sealed record FsDirEntry(string Name, bool IsDirectory, long? Size = null, long? ModifiedAt = null);

/// <summary>
/// Snapshot metadata for a file or directory entry. Timestamps are expressed as milliseconds since
/// the Unix epoch so the JSON surface stays integer-based and Tauri-compatible.
/// </summary>
public sealed record FsStatResult(
    bool IsDirectory,
    bool IsFile,
    bool IsSymlink,
    long Size,
    long? CreatedAt = null,
    long? ModifiedAt = null,
    long? AccessedAt = null);

public sealed record FsMkdirOptions(string Base, string? Path = null, bool Recursive = false);

public sealed record FsCopyOptions(string FromBase, string? FromPath = null, string ToBase = "", string? ToPath = null);

public sealed record FsRenameOptions(string FromBase, string? FromPath = null, string ToBase = "", string? ToPath = null);

public sealed record FsRemoveOptions(string Base, string? Path = null, bool Recursive = false);
