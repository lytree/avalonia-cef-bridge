using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.FileSystem;
using Tarui.Plugins.Events;

namespace Tarui.FileSystem.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            WriteTextRoundTripsAndIsAtomic();
            ReadRejectsFilesOverTheOperationLimit();
            ResourcesBaseRejectsWrites();
            MkdirAndRemoveCoverTreeOperations();
            ReadDirReportsFilesAndDirectories();
            StatAndExistsReflectDiskState();
            CopyAndRenameMoveBytesWithinPolicy();
            ScopeAuthorizerRespectsAllowDenyAndWildcards();
            PluginRegistersAllFourteenCommands();
            ScopeMatcherRejectsCaseDifferingDenyEntriesOnWindows();
            ScopeMatcherMatchesCaseDifferingCandidateOnWindows();
            FileSystemServiceRejectsSymlinkEscapeOnSupportedOs();
            WatchReportsCreationsUntilUnwatched();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.FileSystem self-tests passed.");
        return 0;
    }

    private static void WriteTextRoundTripsAndIsAtomic()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);
        var options = new FsWriteTextOptions(BaseName(root), Path: "hello.txt", Contents: "hello tarui");

        var before = File.Exists(Path.Combine(root.Dir, "hello.txt"));
        service.WriteTextAsync(options, default).AsTask().GetAwaiter().GetResult();

        var read = service.ReadTextAsync(new FsPathOptions(BaseName(root), Path: "hello.txt"), default).AsTask().GetAwaiter().GetResult();
        Assert(!before, "The target must not exist before the write.");
        Assert(read.Contents == "hello tarui", "The text must round-trip through the atomic writer.");

        // Atomic writes leave no stray .tmp files.
        var leftovers = Directory.GetFiles(root.Dir, "*.tmp").Length;
        Assert(leftovers == 0, "The atomic writer must clean up its temporary files.");
    }

    private static void ReadRejectsFilesOverTheOperationLimit()
    {
        using var root = CreateTempRoot();
        var options = new FileAccessPolicyOptions(MaxReadBytes: 16);
        var policy = new FileAccessPolicy(options);
        var service = new FileSystemService(new ScopedPolicy(policy, root));

        var target = Path.Combine(root.Dir, "big.bin");
        File.WriteAllBytes(target, new byte[64]);

        PathDenialReason? captured = null;
        try
        {
            service.ReadTextAsync(new FsPathOptions(BaseName(root), Path: "big.bin"), default).AsTask().GetAwaiter().GetResult();
        }
        catch (PathAccessDeniedException exception)
        {
            captured = exception.Reason;
        }

        Assert(captured == PathDenialReason.SizeLimit,
            $"Reads over the per-operation limit must fail with SizeLimit, but was {captured}.");
    }

    private static void ResourcesBaseRejectsWrites()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root, resourcesDir: root.Dir);

        PathDenialReason? captured = null;
        try
        {
            service.WriteTextAsync(new FsWriteTextOptions("resources", Path: "note.txt", Contents: "x"), default).AsTask().GetAwaiter().GetResult();
        }
        catch (PathAccessDeniedException exception)
        {
            captured = exception.Reason;
        }

        Assert(captured is not null, "Writing to the read-only resources base must be rejected.");
    }

    private static void MkdirAndRemoveCoverTreeOperations()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);

        service.MkdirAsync(new FsMkdirOptions(BaseName(root), Path: "nested/deep", Recursive: true), default).AsTask().GetAwaiter().GetResult();
        Assert(Directory.Exists(Path.Combine(root.Dir, "nested", "deep")), "Recursive mkdir must create the full tree.");

        File.WriteAllText(Path.Combine(root.Dir, "nested", "deep", "a.txt"), "a");
        service.RemoveAsync(new FsRemoveOptions(BaseName(root), Path: "nested", Recursive: true), default).AsTask().GetAwaiter().GetResult();
        Assert(!Directory.Exists(Path.Combine(root.Dir, "nested")), "Recursive remove must delete the whole tree.");
    }

    private static void ReadDirReportsFilesAndDirectories()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);

        Directory.CreateDirectory(Path.Combine(root.Dir, "sub"));
        File.WriteAllText(Path.Combine(root.Dir, "sub", "child.txt"), "12345");

        var entries = service.ReadDirAsync(new FsReadDirOptions(BaseName(root), Path: null, Recursive: true), default).AsTask().GetAwaiter().GetResult();
        var names = entries.Select(static e => e.Name).ToHashSet(StringComparer.Ordinal);
        Assert(names.Contains("sub"), "ReadDir must include subdirectories.");
        Assert(names.Contains("child.txt"), "Recursive ReadDir must include nested files.");

        var child = entries.First(e => e.Name == "child.txt");
        Assert(child.Size == 5, "Entries must carry byte sizes for files.");
    }

    private static void StatAndExistsReflectDiskState()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);

        var missing = service.StatAsync(new FsPathOptions(BaseName(root), Path: "missing.txt"), default).AsTask().GetAwaiter().GetResult();
        Assert(missing is null, "Stat of a missing path must return null.");
        Assert(!service.ExistsAsync(new FsPathOptions(BaseName(root), Path: "missing.txt"), default).AsTask().GetAwaiter().GetResult(),
            "Exists must be false for missing entries.");

        File.WriteAllText(Path.Combine(root.Dir, "hello.txt"), "hi");
        var stat = service.StatAsync(new FsPathOptions(BaseName(root), Path: "hello.txt"), default).AsTask().GetAwaiter().GetResult();
        Assert(stat is not null && stat.IsFile && !stat.IsDirectory, "File stat must report file=true/dir=false.");
        Assert(stat!.Size == 2, "File stat must carry the exact byte length.");
        Assert(service.ExistsAsync(new FsPathOptions(BaseName(root), Path: "hello.txt"), default).AsTask().GetAwaiter().GetResult(),
            "Exists must be true for on-disk files.");
    }

    private static void CopyAndRenameMoveBytesWithinPolicy()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);

        var sourcePath = Path.Combine(root.Dir, "src.txt");
        File.WriteAllText(sourcePath, "payload");

        service.CopyAsync(new FsCopyOptions(BaseName(root), FromPath: "src.txt", ToBase: BaseName(root), ToPath: "copy.txt"), default).AsTask().GetAwaiter().GetResult();
        Assert(File.ReadAllText(Path.Combine(root.Dir, "copy.txt")) == "payload", "Copy must duplicate bytes.");

        service.RenameAsync(new FsRenameOptions(BaseName(root), FromPath: "copy.txt", ToBase: BaseName(root), ToPath: "renamed.txt"), default).AsTask().GetAwaiter().GetResult();
        Assert(!File.Exists(Path.Combine(root.Dir, "copy.txt")), "Rename must remove the source.");
        Assert(File.ReadAllText(Path.Combine(root.Dir, "renamed.txt")) == "payload", "Rename must preserve contents.");
    }

    private static void ScopeAuthorizerRespectsAllowDenyAndWildcards()
    {
        var allow = new PathScope[]
        {
            new(Base: "appData", Path: "documents/**"),
            new(Base: "appConfig", Path: "**/*.json"),
            new(Base: "temp"),
        };
        var deny = new PathScope[] { new(Base: "appData", Path: "documents/secrets/*") };

        Assert(AllowsRead("appData", "documents/reports/2026/q1.md", allow, deny), "** must match nested paths.");
        Assert(AllowsRead("appConfig", "settings/feature.json", allow, deny), "**/*.json must match nested json files.");
        Assert(AllowsRead("appConfig", "settings/deep/nested/config.json", allow, deny), "**/*.json must match any depth of nested json files.");
        Assert(AllowsRead("temp", "anything.dat", allow, deny), "A missing pattern path must allow any path under the base.");
        Assert(!AllowsRead("appData", "documents/secrets/vault.txt", allow, deny), "Deny must win over allow.");
        Assert(!AllowsRead("home", "readme.txt", allow, deny), "Bases not listed in allow must be denied.");
        Assert(!AllowsRead("appConfig", "notes.txt", allow, deny), "Patterns without wildcards still require the extension match.");
    }

    private static void PluginRegistersAllFourteenCommands()
    {
        var builder = new CommandRouterBuilder();
        var service = new RecordingFileSystemService();
        new FileSystemPlugin(service).ConfigureCommands(builder);
        var router = builder.Build();

        var expected = new[]
        {
            "plugin:fs|read-text-file",
            "plugin:fs|write-text-file",
            "plugin:fs|read-dir",
            "plugin:fs|stat",
            "plugin:fs|exists",
            "plugin:fs|mkdir",
            "plugin:fs|copy-file",
            "plugin:fs|rename",
            "plugin:fs|remove",
            "plugin:fs|read-file-stream",
            "plugin:fs|write-begin",
            "plugin:fs|write-chunk",
            "plugin:fs|write-commit",
            "plugin:fs|write-cancel",
            "plugin:fs|watch",
            "plugin:fs|unwatch",
        };

        foreach (var command in expected)
        {
            Assert(router.Commands.Contains(command), $"The plugin must register command '{command}'.");
        }

        Assert(router.RegisteredPermissions.Count == expected.Length,
            "Every file system permission must be registered exactly once with no extras.");
    }


    private static void ScopeMatcherRejectsCaseDifferingDenyEntriesOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var allow = new PathScope[] { new(Base: "appData", Path: "**") };
        var deny = new PathScope[] { new(Base: "appData", Path: "documents/secrets/*") };

        var denied = !FileScopeMatcher.MatchesScope(allow, deny, "appData", "DOCUMENTS/SECRETS/KEY.TXT");
        Assert(denied, "Windows deny entries must match a differently-cased request path case-insensitively.");
    }

    private static void ScopeMatcherMatchesCaseDifferingCandidateOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var allow = new PathScope[] { new(Base: "appData", Path: "*.TXT") };
        var deny = Array.Empty<PathScope>();

        var allowed = FileScopeMatcher.MatchesScope(allow, deny, "appData", "notes.txt");
        Assert(allowed, "Windows allow entries must match a differently-cased candidate path.");
    }

    private static void FileSystemServiceRejectsSymlinkEscapeOnSupportedOs()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var root = CreateTempRoot();
        var outsideDir = Path.Combine(Path.GetTempPath(), "tarui-fs-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDir);
        var outsideFile = Path.Combine(outsideDir, "leaked.txt");
        File.WriteAllText(outsideFile, "secret");

        try
        {
            var allowedDir = Path.Combine(root.Dir, "inside");
            Directory.CreateDirectory(allowedDir);
            var linkPath = Path.Combine(allowedDir, "escape");
            Directory.CreateSymbolicLink(linkPath, outsideFile);

            var service = new FileSystemService(new SymlinkTestPolicy(root));
            var options = new FsWriteTextOptions(BaseName(root), Path: "inside/escape", Contents: "payload");

            PathDenialReason? captured = null;
            try
            {
                service.WriteTextAsync(options, default).AsTask().GetAwaiter().GetResult();
            }
            catch (PathAccessDeniedException exception)
            {
                captured = exception.Reason;
            }

            Assert(
                captured == PathDenialReason.LinkEscape,
                "A symlink escaping the authorized base must surface as LinkEscape, but was " + captured + ".");
        }
        finally
        {
            try { Directory.Delete(outsideDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class SymlinkTestPolicy : IFileAccessPolicy
    {
        private readonly TestRoot _root;

        public SymlinkTestPolicy(TestRoot root)
        {
            _root = root;
        }

        public bool TryGetBaseDirectory(string baseName, out string directoryPath, out bool isReadOnly)
        {
            if (string.Equals(baseName, _root.BaseName, StringComparison.Ordinal))
            {
                directoryPath = _root.Dir;
                isReadOnly = false;
                return true;
            }

            directoryPath = string.Empty;
            isReadOnly = false;
            return false;
        }

        public string Authorize(FileAccessKind kind, string baseDirectory, string requestPath)
        {
            if (string.IsNullOrEmpty(requestPath))
            {
                return Path.GetFullPath(baseDirectory);
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, requestPath));
        }

        public string? ResolveBase(string baseName)
        {
            if (string.Equals(baseName, _root.BaseName, StringComparison.Ordinal))
            {
                return _root.Dir;
            }
            return null;
        }

        public bool IsWithinOperationLimit(FileAccessKind kind, long byteCount) => byteCount >= 0 && byteCount < 4096;
        public bool TryReserveTotalBytes(long byteCount) => byteCount >= 0 && byteCount < 4096;
        public void ReleaseTotalBytes(long byteCount) { }
        public Task WriteAllBytesAtomicAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default) => File.WriteAllTextAsync(targetPath, System.Text.Encoding.UTF8.GetString(content.ToArray()), cancellationToken);
    }

    private static bool AllowsRead(string baseName, string path, PathScope[] allow, PathScope[] deny)
    {
        var allowList = allow;
        var denyList = deny;

        foreach (var scope in denyList)
        {
            if (MatchesOne(scope, baseName, path))
            {
                return false;
            }
        }

        if (allowList.Length == 0)
        {
            return true;
        }

        foreach (var scope in allowList)
        {
            if (MatchesOne(scope, baseName, path))
            {
                return true;
            }
        }

        return false;

        static bool MatchesOne(PathScope scope, string? baseName, string? requestPath)
        {
            var relative = requestPath ?? string.Empty;
            if (!string.IsNullOrEmpty(scope.Base) &&
                !string.Equals(scope.Base, baseName, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrEmpty(scope.Path))
            {
                return true;
            }

            return MatchGlob(scope.Path, relative);
        }

        static bool MatchGlob(string pattern, string candidate)
        {
            var patternSegments = pattern.Replace('\\', '/').Split('/', StringSplitOptions.None);
            var candidateSegments = candidate.Replace('\\', '/').Split('/', StringSplitOptions.None);
            return MatchSegments(patternSegments.AsSpan(), candidateSegments.AsSpan());
        }

        static bool MatchSegments(ReadOnlySpan<string> pattern, ReadOnlySpan<string> candidate)
        {
            while (pattern.Length > 0)
            {
                if (pattern[0] == "**")
                {
                    // Consume the leading ** and try every candidate suffix (including empty)
                    // against the remaining pattern.
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

        static bool MatchSegment(string patternSegment, string candidateSegment)
        {
            if (patternSegment == "*")
            {
                return candidateSegment.Length > 0;
            }

            var starIndex = patternSegment.IndexOf('*');
            if (starIndex < 0)
            {
                return string.Equals(patternSegment, candidateSegment, StringComparison.Ordinal);
            }

            // Simple prefix/suffix match for a single '*' within a segment.
            var prefix = patternSegment[..starIndex];
            var suffix = patternSegment[(starIndex + 1)..];
            if (suffix.Contains('*'))
            {
                return string.Equals(patternSegment, candidateSegment, StringComparison.Ordinal);
            }

            return candidateSegment.StartsWith(prefix, StringComparison.Ordinal) &&
                   candidateSegment.EndsWith(suffix, StringComparison.Ordinal) &&
                   candidateSegment.Length >= prefix.Length + suffix.Length;
        }
    }

    private static void WatchReportsCreationsUntilUnwatched()
    {
        using var root = CreateTempRoot();
        var events = new FakeEventSender();
        var service = new FileSystemService(new ScopedPolicy(new FileAccessPolicy(), root), events);
        try
        {
            var result = service.WatchAsync("main", new FsWatchOptions(BaseName(root)), default).AsTask().GetAwaiter().GetResult();
            Assert(!string.IsNullOrWhiteSpace(result.WatchId), "A watch must return a stable handle.");

            File.WriteAllText(Path.Combine(root.Dir, "new.txt"), "hello");
            Assert(
                WaitUntil(() => events.HasCreated("new.txt"), timeoutMs: 8000),
                "Creating a file under the watched directory must emit a created fs://watch-change event.");

            var payload = events.FirstCreated("new.txt");
            Assert(payload.WatchId == result.WatchId, "The event must carry the watch handle.");
            Assert(payload.EventKind == FsWatchEventKinds.Created, "The event must be reported as created.");
            Assert(payload.OutputPaths.Contains("new.txt", StringComparer.Ordinal), "The event must carry the relative path.");
        }
        finally
        {
            service.Dispose();
        }
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return condition();
    }

    private sealed class FakeEventSender : IEventSender
    {
        private readonly object _gate = new();
        public List<(string Name, FsWatchEvent Event)> Events { get; } = [];

        public ValueTask<Unit> EmitAsync(string eventName, JsonElement payload, string? targetWindow, CancellationToken cancellationToken)
        {
            var watchEvent = payload.Deserialize(TaruiJsonContext.Default.FsWatchEvent)!;
            lock (_gate)
            {
                Events.Add((eventName, watchEvent));
            }

            return ValueTask.FromResult(new Unit());
        }

        public bool HasCreated(string path)
        {
            lock (_gate)
            {
                return Events.Any(e => e.Name == "fs://watch-change" &&
                                       e.Event.EventKind == FsWatchEventKinds.Created &&
                                       e.Event.OutputPaths.Contains(path, StringComparer.Ordinal));
            }
        }

        public FsWatchEvent FirstCreated(string path)
        {
            lock (_gate)
            {
                return Events.First(e => e.Name == "fs://watch-change" &&
                                         e.Event.EventKind == FsWatchEventKinds.Created &&
                                         e.Event.OutputPaths.Contains(path, StringComparer.Ordinal)).Event;
            }
        }
    }

    private static FileSystemService CreateService(TestRoot root, string? resourcesDir = null)
    {
        var policy = new FileAccessPolicy();
        return new FileSystemService(new ScopedPolicy(policy, root, resourcesDir));
    }

    private static string BaseName(TestRoot root) => root.BaseName;

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static TestRoot CreateTempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tarui-fs-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return new TestRoot(dir);
    }

    private sealed class TestRoot : IDisposable
    {
        public TestRoot(string dir)
        {
            Dir = dir;
            BaseName = $"__fs_test_{Guid.NewGuid():N}";
        }

        public string Dir { get; }
        public string BaseName { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch
            {
                // Best effort cleanup on Windows anti-virus holds.
            }
        }
    }

    /// <summary>Wraps the real FileAccessPolicy so the test base name maps to the temporary directory.</summary>
    private sealed class ScopedPolicy : IFileAccessPolicy
    {
        private readonly FileAccessPolicy _inner;
        private readonly TestRoot _root;
        private readonly string? _resourcesDir;

        public ScopedPolicy(FileAccessPolicy inner, TestRoot root, string? resourcesDir = null)
        {
            _inner = inner;
            _root = root;
            _resourcesDir = resourcesDir;
        }

        public bool TryGetBaseDirectory(string baseName, out string directoryPath, out bool isReadOnly)
        {
            if (string.Equals(baseName, _root.BaseName, StringComparison.Ordinal))
            {
                directoryPath = _root.Dir;
                isReadOnly = false;
                return true;
            }

            if (string.Equals(baseName, "resources", StringComparison.Ordinal) && _resourcesDir is not null)
            {
                directoryPath = _resourcesDir;
                isReadOnly = true;
                return true;
            }

            return _inner.TryGetBaseDirectory(baseName, out directoryPath, out isReadOnly);
        }

        public string? ResolveBase(string baseName)
        {
            if (string.Equals(baseName, _root.BaseName, StringComparison.Ordinal))
            {
                return _root.Dir;
            }

            if (string.Equals(baseName, "resources", StringComparison.Ordinal) && _resourcesDir is not null)
            {
                return _resourcesDir;
            }

            return _inner.ResolveBase(baseName);
        }

        public string Authorize(FileAccessKind kind, string baseDirectory, string requestPath) => _inner.Authorize(kind, baseDirectory, requestPath);

        public bool IsWithinOperationLimit(FileAccessKind kind, long byteCount) => _inner.IsWithinOperationLimit(kind, byteCount);

        public bool TryReserveTotalBytes(long byteCount) => _inner.TryReserveTotalBytes(byteCount);

        public void ReleaseTotalBytes(long byteCount) => _inner.ReleaseTotalBytes(byteCount);

        public Task WriteAllBytesAtomicAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default) =>
            _inner.WriteAllBytesAtomicAsync(targetPath, content, cancellationToken);
    }

    private sealed class RecordingFileSystemService : IFileSystemService
    {
        public List<string> Calls { get; } = [];

        private void Record(string name) => Calls.Add(name);

        public ValueTask<FsReadTextResult> ReadTextAsync(FsPathOptions options, CancellationToken cancellationToken) { Record("read-text"); return ValueTask.FromResult(new FsReadTextResult("")); }
        public ValueTask<Unit> WriteTextAsync(FsWriteTextOptions options, CancellationToken cancellationToken) { Record("write-text"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<FsDirEntry[]> ReadDirAsync(FsReadDirOptions options, CancellationToken cancellationToken) { Record("read-dir"); return ValueTask.FromResult<FsDirEntry[]>([]); }
        public ValueTask<FsStatResult?> StatAsync(FsPathOptions options, CancellationToken cancellationToken) { Record("stat"); return ValueTask.FromResult<FsStatResult?>(null); }
        public ValueTask<bool> ExistsAsync(FsPathOptions options, CancellationToken cancellationToken) { Record("exists"); return ValueTask.FromResult(false); }
        public ValueTask<Unit> MkdirAsync(FsMkdirOptions options, CancellationToken cancellationToken) { Record("mkdir"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<Unit> CopyAsync(FsCopyOptions options, CancellationToken cancellationToken) { Record("copy"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<Unit> RenameAsync(FsRenameOptions options, CancellationToken cancellationToken) { Record("rename"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<Unit> RemoveAsync(FsRemoveOptions options, CancellationToken cancellationToken) { Record("remove"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<FsStreamResult> ReadFileStreamAsync(FsReadStreamOptions options, CancellationToken cancellationToken) { Record("read-file-stream"); return ValueTask.FromResult(new FsStreamResult(0)); }
        public ValueTask<FsWriteBeginResult> WriteBeginAsync(FsWriteBeginOptions options, string windowLabel, CancellationToken cancellationToken) { Record("write-begin"); return ValueTask.FromResult(new FsWriteBeginResult("sess-1")); }
        public ValueTask<Unit> WriteChunkAsync(FsWriteChunkOptions options, CancellationToken cancellationToken) { Record("write-chunk"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<Unit> WriteCommitAsync(FsWriteCommitOptions options, CancellationToken cancellationToken) { Record("write-commit"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<Unit> WriteCancelAsync(FsWriteCancelOptions options, CancellationToken cancellationToken) { Record("write-cancel"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<FsWatchResult> WatchAsync(string windowLabel, FsWatchOptions options, CancellationToken cancellationToken) { Record("watch"); return ValueTask.FromResult(new FsWatchResult("fsw-watch")); }
        public ValueTask<Unit> UnwatchAsync(FsUnwatchOptions options, CancellationToken cancellationToken) { Record("unwatch"); return ValueTask.FromResult(new Unit()); }
    }
}
