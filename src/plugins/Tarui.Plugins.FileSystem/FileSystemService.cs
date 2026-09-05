using System.Globalization;
using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Events;

namespace Tarui.Plugins.FileSystem;

/// <summary>
/// Default file system service. Every operation routes through <see cref="IFileAccessPolicy"/> so
/// rooted paths, device paths, link escapes, size limits, and read-only bases are rejected before any
/// disk call. Writes are durable via the atomic temporary-file replacement exposed by the policy.
/// </summary>
public sealed class FileSystemService(IFileAccessPolicy policy, IEventSender? events = null) : IFileSystemService, IDisposable
{
    private const long DefaultChunkBytes = 256 * 1024;
    private const long MinChunkBytes = 1;
    private const long MaxChunkBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan WriteIdleTimeout = TimeSpan.FromMinutes(10);
    private const string WatchChangeEvent = "fs://watch-change";

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingWrite> _pendingWrites = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, WatcherSession> _watchers = new(StringComparer.Ordinal);
    private readonly IEventSender? _events = events;
    private int _disposed;

    public async ValueTask<FsReadTextResult> ReadTextAsync(FsPathOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveAuthorized(FileAccessKind.Read, options.Base, options.Path);
        var length = new FileInfo(path).Length;
        if (!policy.IsWithinOperationLimit(FileAccessKind.Read, length))
        {
            throw new PathAccessDeniedException(PathDenialReason.SizeLimit,
                "The file exceeds the per-operation read size limit.");
        }

        return new FsReadTextResult(await File.ReadAllTextAsync(path, cancellationToken));
    }

    public async ValueTask<FsStreamResult> ReadFileStreamAsync(
        FsReadStreamOptions options,
        CancellationToken cancellationToken)
    {
        var path = ResolveAuthorized(FileAccessKind.Read, options.Base, options.Path);
        var size = new FileInfo(path).Length;

        // 流式读豁免 per-operation 8 MiB 上限（大文件按块推送），但保留累计预算以阻止
        // 并发读掏空磁盘。预算按文件整体预定，一次性判定，与单文件单调递增一致。
        if (!policy.TryReserveTotalBytes(size))
        {
            throw new PathAccessDeniedException(PathDenialReason.SizeLimit,
                "The file exceeds the cumulative read size budget.");
        }

        var channel = ChannelContext.Bind<FsStreamEvent>(options.Channel);
        try
        {
            var modifiedAt = EpochMs(File.GetLastWriteTimeUtc(path));
            await channel.SendAsync(new FsStreamEvent("meta", new FsStreamMeta(size, modifiedAt)), cancellationToken);
            if (size > 0)
            {
                var chunkBytes = Math.Clamp(options.ChunkBytes ?? DefaultChunkBytes, MinChunkBytes, MaxChunkBytes);
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 81920, useAsync: true);
                var buffer = new byte[chunkBytes];
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await channel.SendAsync(new FsStreamEvent("chunk", Data: buffer.AsMemory(0, read).ToArray()),
                        cancellationToken);
                }
            }

            // Handler 正常返回即 resolve，前端据此判定流成功结束。
            return new FsStreamResult(size);
        }
        finally
        {
            policy.ReleaseTotalBytes(size);
        }
    }

    public async ValueTask<Unit> WriteTextAsync(FsWriteTextOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveAuthorized(FileAccessKind.Write, options.Base, options.Path);
        var bytes = System.Text.Encoding.UTF8.GetBytes(options.Contents ?? string.Empty);
        await policy.WriteAllBytesAtomicAsync(path, bytes, cancellationToken);
        return new Unit();
    }

    public ValueTask<FsWriteBeginResult> WriteBeginAsync(
        FsWriteBeginOptions options,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SweepExpired();

        var target = ResolveAuthorized(FileAccessKind.Write, options.Base, options.Path);
        var directory = Path.GetDirectoryName(target) ?? ".";
        var fileName = Path.GetFileName(target);
        var tmp = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");

        // 可选：按声明的总字节一次性预定累积预算；未声明则保留为 0，随后逐 chunk 预定。
        var reserved = options.TotalBytes ?? 0;
        if (reserved > 0 && !policy.TryReserveTotalBytes(reserved))
        {
            throw new PathAccessDeniedException(PathDenialReason.SizeLimit,
                "The write exceeds the cumulative size budget.");
        }

        try
        {
            var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);
            var session = new PendingWrite(
                writeId: NewWriteId(),
                windowLabel: windowLabel,
                targetPath: target,
                tmpPath: tmp,
                stream: stream,
                totalBytes: options.TotalBytes,
                reservedBytes: reserved,
                lastTouch: DateTimeOffset.UtcNow);
            _pendingWrites[session.WriteId] = session;
            return ValueTask.FromResult(new FsWriteBeginResult(session.WriteId));
        }
        catch
        {
            policy.ReleaseTotalBytes(reserved);
            TryDeleteFile(tmp);
            throw;
        }
    }

    public ValueTask<Unit> WriteChunkAsync(FsWriteChunkOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SweepExpired();

        var stream = CreateStream(options.WriteId);

        lock (stream)
        {
            if (options.Sequence != stream.NextSequence)
            {
                throw new PathAccessDeniedException(PathDenialReason.IllegalSegment,
                    $"Chunk {options.Sequence} is out of order; expected {stream.NextSequence}.");
            }

            if (!policy.IsWithinOperationLimit(FileAccessKind.Write, options.Data.Length))
            {
                throw new PathAccessDeniedException(PathDenialReason.SizeLimit,
                    "A single write chunk exceeds the per-operation size limit.");
            }

            if (stream.TotalBytes is not null && stream.ReceivedBytes + options.Data.Length > stream.TotalBytes)
            {
                throw new PathAccessDeniedException(PathDenialReason.SizeLimit,
                    "The chunk exceeds the declared total byte count.");
            }

            if (stream.TotalBytes is null)
            {
                if (!policy.TryReserveTotalBytes(options.Data.Length))
                {
                    throw new PathAccessDeniedException(PathDenialReason.SizeLimit,
                        "The write exceeds the cumulative size budget.");
                }

                stream.ReservedBytes += options.Data.Length;
            }

            stream.Stream.Write(options.Data.AsSpan());
            stream.ReceivedBytes += options.Data.Length;
            stream.NextSequence++;
            stream.LastTouch = DateTimeOffset.UtcNow;
        }

        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> WriteCommitAsync(FsWriteCommitOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_pendingWrites.TryRemove(options.WriteId, out var session))
        {
            throw new InvalidOperationException($"No open write session '{options.WriteId}'.");
        }

        try
        {
            session.Stream.Flush();
            session.Stream.Dispose();
            File.Move(session.TmpPath, session.TargetPath, overwrite: true);
            return ValueTask.FromResult(new Unit());
        }
        catch
        {
            TryDeleteFile(session.TmpPath);
            throw;
        }
        finally
        {
            policy.ReleaseTotalBytes(session.ReservedBytes);
        }
    }

    public ValueTask<Unit> WriteCancelAsync(FsWriteCancelOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_pendingWrites.TryRemove(options.WriteId, out var session))
        {
            throw new InvalidOperationException($"No open write session '{options.WriteId}'.");
        }

        session.Stream.Dispose();
        TryDeleteFile(session.TmpPath);
        policy.ReleaseTotalBytes(session.ReservedBytes);
        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<FsWatchResult> WatchAsync(
        string windowLabel,
        FsWatchOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var root = ResolveAuthorized(FileAccessKind.Read, options.Base, options.Path);
        if (!Directory.Exists(root))
        {
            root = Path.GetDirectoryName(root) ?? root;
        }

        var watcher = new FileSystemWatcher
        {
            Path = root,
            IncludeSubdirectories = options.Recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
        };
        var session = new WatcherSession(NewWatchId(), windowLabel, watcher, _events);
        watcher.Created += (_, e) => session.Emit(FsWatchEventKinds.Created, [Relative(root, e.FullPath)]);
        watcher.Changed += (_, e) => session.Emit(FsWatchEventKinds.Changed, [Relative(root, e.FullPath)]);
        watcher.Deleted += (_, e) => session.Emit(FsWatchEventKinds.Deleted, [Relative(root, e.FullPath)]);
        watcher.Renamed += (_, e) => session.Emit(FsWatchEventKinds.Renamed, [Relative(root, e.OldFullPath), Relative(root, e.FullPath)]);
        watcher.Error += (_, e) => session.Emit(FsWatchEventKinds.Error, [e.GetException().Message]);
        _watchers[session.WatchId] = session;
        watcher.EnableRaisingEvents = true;
        return ValueTask.FromResult(new FsWatchResult(session.WatchId));
    }

    public ValueTask<Unit> UnwatchAsync(FsUnwatchOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_watchers.TryRemove(options.WatchId, out var session))
        {
            session.Dispose();
        }

        return ValueTask.FromResult(new Unit());
    }

    private static void DisposeWatchers(System.Collections.Concurrent.ConcurrentDictionary<string, WatcherSession> watchers)
    {
        foreach (var (id, session) in watchers)
        {
            if (watchers.TryRemove(id, out var removed))
            {
                removed.Dispose();
            }
        }
    }

    /// <summary>Abandons every open write session owned by <paramref name="windowLabel"/>, deleting temp files.</summary>
    public void CleanupWindow(string windowLabel)
    {
        foreach (var (id, session) in _pendingWrites)
        {
            if (string.Equals(session.WindowLabel, windowLabel, StringComparison.Ordinal))
            {
                if (_pendingWrites.TryRemove(id, out var removed))
                {
                    Abandon(removed);
                }
            }
        }

        foreach (var (id, session) in _watchers)
        {
            if (string.Equals(session.WindowLabel, windowLabel, StringComparison.Ordinal) &&
                _watchers.TryRemove(id, out var removed))
            {
                removed.Dispose();
            }
        }
    }

    /// <summary>Disposes open streams and watchers, removes temp files, and releases reserved budget on teardown.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var (id, session) in _pendingWrites)
        {
            if (_pendingWrites.TryRemove(id, out var removed))
            {
                Abandon(removed);
            }
        }

        DisposeWatchers(_watchers);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);

    private PendingWrite CreateStream(string writeId) =>
        _pendingWrites.TryGetValue(writeId, out var stream)
            ? stream
            : throw new InvalidOperationException($"No open write session '{writeId}'.");

    /// <summary>Reclaims sessions whose last activity predates the idle timeout (opportunistic, lock-free under clear).</summary>
    private void SweepExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, session) in _pendingWrites)
        {
            if (now - session.LastTouch > WriteIdleTimeout && _pendingWrites.TryRemove(id, out var removed))
            {
                Abandon(removed);
            }
        }
    }

    private void Abandon(PendingWrite session)
    {
        session.Stream.Dispose();
        TryDeleteFile(session.TmpPath);
        policy.ReleaseTotalBytes(session.ReservedBytes);
    }

    private static string NewWriteId() => "fw-" + Guid.NewGuid().ToString("N");

    private static string NewWatchId() => "fsw-" + Guid.NewGuid().ToString("N");

    private static string Relative(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        return relative == "." ? string.Empty : relative.Replace('\\', '/');
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 删除暂存文件尽力而为：失败时无非托管泄漏，剩余 tmp 由后续 sweep/清理接管。
        }
    }

    /// <summary>A single, serialized chunked-write session buffered to a temporary file.</summary>
    private sealed class PendingWrite(
        string writeId,
        string windowLabel,
        string targetPath,
        string tmpPath,
        FileStream stream,
        long? totalBytes,
        long reservedBytes,
        DateTimeOffset lastTouch)
    {
        public string WriteId { get; } = writeId;
        public string WindowLabel { get; } = windowLabel;
        public string TargetPath { get; } = targetPath;
        public string TmpPath { get; } = tmpPath;
        public FileStream Stream { get; } = stream;
        public long? TotalBytes { get; } = totalBytes;
        public DateTimeOffset LastTouch { get; set; } = lastTouch;
        public long NextSequence { get; set; }
        public long ReceivedBytes { get; set; }
        public long ReservedBytes { get; set; } = reservedBytes;
    }

    /// <summary>
    /// A single active directory watch. Binds the watcher's change events to <c>fs://watch-change</c> delivery,
    /// scoped to the owning window, for the lifetime of the watch. Dispose stops the native watcher.
    /// </summary>
    private sealed class WatcherSession : IDisposable
    {
        private readonly string _watchId;
        private readonly string _windowLabel;
        private readonly FileSystemWatcher _watcher;
        private readonly IEventSender? _events;

        public WatcherSession(
            string watchId,
            string windowLabel,
            FileSystemWatcher watcher,
            IEventSender? events)
        {
            _watchId = watchId;
            _windowLabel = windowLabel;
            _watcher = watcher;
            _events = events;
        }

        public string WatchId => _watchId;

        public string WindowLabel => _windowLabel;

        public void Emit(string kind, string[] outputPaths)
        {
            if (_events is null)
            {
                return;
            }

            var payload = JsonSerializer.SerializeToElement(
                new FsWatchEvent(_watchId, kind, outputPaths),
                TaruiJsonContext.Default.FsWatchEvent);
            FireAndForget.Run(_events.EmitAsync("fs://watch-change", payload, _windowLabel, CancellationToken.None).AsTask());
        }

        public void Dispose() => _watcher.Dispose();
    }

    public ValueTask<FsDirEntry[]> ReadDirAsync(FsReadDirOptions options, CancellationToken cancellationToken)
    {
        var root = ResolveAuthorized(FileAccessKind.Read, options.Base, options.Path);
        if (!Directory.Exists(root))
        {
            return ValueTask.FromResult<FsDirEntry[]>([]);
        }

        var entries = new List<FsDirEntry>();
        AddEntries(root, entries, options.Recursive, baseDir: root);
        return ValueTask.FromResult<FsDirEntry[]>([.. entries]);
    }

    public ValueTask<FsStatResult?> StatAsync(FsPathOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveAuthorized(FileAccessKind.Read, options.Base, options.Path);
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            var isSymlink = info.LinkTarget is not null;
            return ValueTask.FromResult<FsStatResult?>(new FsStatResult(
                IsDirectory: false,
                IsFile: true,
                IsSymlink: isSymlink,
                Size: info.Length,
                CreatedAt: EpochMs(info.CreationTimeUtc),
                ModifiedAt: EpochMs(info.LastWriteTimeUtc),
                AccessedAt: EpochMs(info.LastAccessTimeUtc)));
        }

        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            var isSymlink = info.LinkTarget is not null;
            return ValueTask.FromResult<FsStatResult?>(new FsStatResult(
                IsDirectory: true,
                IsFile: false,
                IsSymlink: isSymlink,
                Size: 0,
                CreatedAt: EpochMs(info.CreationTimeUtc),
                ModifiedAt: EpochMs(info.LastWriteTimeUtc),
                AccessedAt: EpochMs(info.LastAccessTimeUtc)));
        }

        return ValueTask.FromResult<FsStatResult?>(null);
    }

    public ValueTask<bool> ExistsAsync(FsPathOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveAuthorized(FileAccessKind.Read, options.Base, options.Path);
        return ValueTask.FromResult(File.Exists(path) || Directory.Exists(path));
    }

    public ValueTask<Unit> MkdirAsync(FsMkdirOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveAuthorized(FileAccessKind.Write, options.Base, options.Path);
        if (options.Recursive)
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            if (Directory.Exists(path))
            {
                // Treat "already exists" as success for non-recursive mkdir to keep the API idempotent.
            }
            else if (Path.GetDirectoryName(path) is string parent && Directory.Exists(parent))
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                throw new DirectoryNotFoundException(
                    "The parent directory does not exist. Pass recursive=true to create it.");
            }
        }

        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> CopyAsync(FsCopyOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.ToBase))
        {
            throw new InvalidOperationException("The destination base directory is required.");
        }

        var from = ResolveAuthorized(FileAccessKind.Read, options.FromBase, options.FromPath);
        var to = ResolveAuthorized(FileAccessKind.Write, options.ToBase, options.ToPath);
        File.Copy(from, to, overwrite: true);
        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> RenameAsync(FsRenameOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.ToBase))
        {
            throw new InvalidOperationException("The destination base directory is required.");
        }

        var from = ResolveAuthorized(FileAccessKind.Write, options.FromBase, options.FromPath);
        var to = ResolveAuthorized(FileAccessKind.Write, options.ToBase, options.ToPath);
        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
        }
        else
        {
            File.Move(from, to, overwrite: true);
        }

        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> RemoveAsync(FsRemoveOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveAuthorized(FileAccessKind.Write, options.Base, options.Path);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: options.Recursive);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }

        return ValueTask.FromResult(new Unit());
    }

    private string ResolveAuthorized(FileAccessKind kind, string baseName, string? requestPath)
    {
        if (!policy.TryGetBaseDirectory(baseName, out var baseDir, out var isReadOnly))
        {
            throw new InvalidOperationException(
                "Base directory '" + baseName + "' is not available on this system.");
        }

        if (kind == FileAccessKind.Write && isReadOnly)
        {
            throw new PathAccessDeniedException(PathDenialReason.OutsideBase,
                "Base directory '" + baseName + "' is read-only.");
        }

        // Ensure the base directory exists before authorizing under it.
        Directory.CreateDirectory(baseDir);
        var resolved = policy.Authorize(kind, baseDir, requestPath ?? string.Empty);

        // P0-03 path B: the lexical path is now safe; resolve any symlinks in that final path and
        // re-confirm the real target still sits inside the real base root.
        var baseReal = ResolveRealDirectory(baseDir);
        var resolvedReal = ResolveRealPath(resolved);
        if (!IsWithinBase(resolvedReal, baseReal))
        {
            throw new PathAccessDeniedException(PathDenialReason.LinkEscape,
                "A symbolic link or reparse point escapes the authorized base directory.");
        }

        return resolved;
    }

    private static string ResolveRealDirectory(string directory)
    {
        var trimmed = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        return ResolveRealPath(trimmed);
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

    private static void AddEntries(string directory, List<FsDirEntry> entries, bool recursive, string baseDir)
    {
        foreach (var file in Directory.GetFiles(directory))
        {
            var info = new FileInfo(file);
            entries.Add(new FsDirEntry(
                info.Name,
                IsDirectory: false,
                Size: info.Length,
                ModifiedAt: EpochMs(info.LastWriteTimeUtc)));
        }

        foreach (var dir in Directory.GetDirectories(directory))
        {
            var info = new DirectoryInfo(dir);
            entries.Add(new FsDirEntry(info.Name, IsDirectory: true, ModifiedAt: EpochMs(info.LastWriteTimeUtc)));
            if (recursive)
            {
                AddEntries(dir, entries, recursive: true, baseDir);
            }
        }
    }

    private static long? EpochMs(DateTime time)
    {
        if (time == DateTime.MinValue || time == DateTime.UnixEpoch)
        {
            return null;
        }

        var ms = (time.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds;
        return ms < 0 ? null : (long)ms;
    }
}
