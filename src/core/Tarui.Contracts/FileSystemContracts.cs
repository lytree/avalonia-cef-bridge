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

/// <summary>
/// Options for streaming a file back to the front-end in chunks: <see cref="Base"/> names a known base
/// directory, <see cref="Path"/> is the user-supplied relative path under that base, <see cref="ChunkBytes"/>
/// overrides the default chunk size, and <see cref="Channel"/> carries the front-end channel token to bind.
/// </summary>
public sealed record FsReadStreamOptions(
    string Base,
    string? Path = null,
    long? ChunkBytes = null,
    string? Channel = null);

/// <summary>Leading metadata frame of a file stream: total byte count plus last-modified time.</summary>
public sealed record FsStreamMeta(long Size, long? ModifiedAt = null);

/// <summary>
/// A single frame streamed over a <c>TaruiChannel</c>. <c>Kind</c> is <c>"meta"</c> for the leading
/// <see cref="Meta"/> frame and <c>"chunk"</c> for each <see cref="Data"/> slice; the final success resolve
/// signals the end of the stream.
/// </summary>
public sealed record FsStreamEvent(string Kind, FsStreamMeta? Meta = null, byte[]? Data = null);

/// <summary>Result of a streamed read, surfaced when the stream completes successfully.</summary>
public sealed record FsStreamResult(long Size);

/// <summary>
/// Starts a chunked write to <see cref="Base"/>/<see cref="Path"/>. <see cref="TotalBytes"/> may be supplied
/// up front so the cumulative budget can be reserved once; when omitted it is reserved incrementally per chunk.
/// Writes are buffered to a temporary file and only become visible on the matching <c>write-commit</c>.
/// </summary>
public sealed record FsWriteBeginOptions(string Base, string? Path = null, long? TotalBytes = null);

/// <summary>Identifies the open write session; pass it to <c>write-chunk</c>/<c>write-commit</c>/<c>write-cancel</c>.</summary>
public sealed record FsWriteBeginResult(string WriteId);

/// <summary>A single data slice for an open write session. <see cref="Sequence"/> must equal the session's
/// next expected index (0-based) so out-of-order, dropped or replayed chunks are rejected.</summary>
public sealed record FsWriteChunkOptions(string WriteId, byte[] Data, long Sequence);

/// <summary>Commits the buffered temporary file to its final path via atomic replace.</summary>
public sealed record FsWriteCommitOptions(string WriteId);

/// <summary>Abandons the open write session, deleting the buffered temporary file.</summary>
public sealed record FsWriteCancelOptions(string WriteId);
