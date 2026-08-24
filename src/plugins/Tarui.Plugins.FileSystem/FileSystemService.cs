using System.Globalization;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.FileSystem;

/// <summary>
/// Default file system service. Every operation routes through <see cref="IFileAccessPolicy"/> so
/// rooted paths, device paths, link escapes, size limits, and read-only bases are rejected before any
/// disk call. Writes are durable via the atomic temporary-file replacement exposed by the policy.
/// </summary>
public sealed class FileSystemService(IFileAccessPolicy policy) : IFileSystemService
{
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

    public async ValueTask<Unit> WriteTextAsync(FsWriteTextOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveAuthorized(FileAccessKind.Write, options.Base, options.Path);
        var bytes = System.Text.Encoding.UTF8.GetBytes(options.Contents ?? string.Empty);
        await policy.WriteAllBytesAtomicAsync(path, bytes, cancellationToken);
        return new Unit();
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
