using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Store;

/// <summary>
/// Default store service. Each distinct (Base, Path) resolves to a canonical file via
/// <c>IFileAccessPolicy</c> (rejecting rooted paths, link escapes, and read-only bases) and is loaded
/// once into an in-memory dictionary.
///
/// Read-family commands return the latest in-memory state under a short read lock. Write-family
/// commands run end-to-end under a per-path <see cref="SemaphoreSlim"/> so mutation, snapshot, and
/// atomic persist are serialized for the same backing file: two concurrent Set calls on the
/// same store cannot interleave a snapshot with the next mutation, eliminating the race where a
/// stale snapshot completes after a newer one and silently rewrites the file with old data. The
/// in-memory dict is rolled back on persist failure, so callers see the next Get reflect the
/// last successful persist.
/// </summary>
public sealed class JsonStoreService : IStoreService, IDisposable
{
    private static readonly JsonTypeInfo<Dictionary<string, string?>> DirectoryTypeInfo =
        (JsonTypeInfo<Dictionary<string, string?>>)StoreFileJsonContext.Default.GetTypeInfo(typeof(Dictionary<string, string?>))!;

    private readonly IFileAccessPolicy _policy;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, StoreEntry> _stores = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemaphoreSlim> _writeLocks = new(StringComparer.Ordinal);
    private bool _disposed;

    public JsonStoreService(IFileAccessPolicy policy)
    {
        _policy = policy;
    }

    public ValueTask<StoreGetResult> GetAsync(StoreKeyOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveReadPath(options.Base, options.Path);
        var snapshot = Snapshot(path);
        var value = snapshot.TryGetValue(options.Key, out var current) ? current : null;
        return ValueTask.FromResult(new StoreGetResult(value));
    }

    public async ValueTask<Unit> SetAsync(StoreSetOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveWritePath(options.Base, options.Path);
        var gate = AcquireWriteLock(path);
        try
        {
            var before = Snapshot(path);
            var after = new Dictionary<string, string?>(before, StringComparer.Ordinal);
            if (options.Value is null)
            {
                after.Remove(options.Key);
            }
            else
            {
                after[options.Key] = options.Value;
            }

            await CommitAsync(path, after, cancellationToken);
            return new Unit();
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask<StoreHasResult> HasAsync(StoreKeyOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveReadPath(options.Base, options.Path);
        var snapshot = Snapshot(path);
        return ValueTask.FromResult(new StoreHasResult(snapshot.ContainsKey(options.Key)));
    }

    public async ValueTask<Unit> DeleteAsync(StoreKeyOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveWritePath(options.Base, options.Path);
        var gate = AcquireWriteLock(path);
        try
        {
            var before = Snapshot(path);
            if (!before.ContainsKey(options.Key))
            {
                return new Unit();
            }

            var after = new Dictionary<string, string?>(before, StringComparer.Ordinal);
            after.Remove(options.Key);
            await CommitAsync(path, after, cancellationToken);
            return new Unit();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<Unit> ClearAsync(StoreFileOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveWritePath(options.Base, options.Path);
        var gate = AcquireWriteLock(path);
        try
        {
            var before = Snapshot(path);
            if (before.Count == 0)
            {
                return new Unit();
            }

            var after = new Dictionary<string, string?>(StringComparer.Ordinal);
            await CommitAsync(path, after, cancellationToken);
            return new Unit();
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask<StoreKeysResult> KeysAsync(StoreFileOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveReadPath(options.Base, options.Path);
        var snapshot = Snapshot(path);
        return ValueTask.FromResult(new StoreKeysResult([.. snapshot.Keys]));
    }

    private Dictionary<string, string?> Snapshot(string path)
    {
        lock (_cacheGate)
        {
            if (!_stores.TryGetValue(path, out var entry))
            {
                entry = new StoreEntry(Load(path));
                _stores[path] = entry;
            }

            return entry.Snapshot;
        }
    }

    private async Task CommitAsync(
        string path,
        Dictionary<string, string?> after,
        CancellationToken cancellationToken)
    {
        // Persist first; only swap the cache once the file is durable. A persist failure leaves
        // the cache on the previous snapshot and the exception is surfaced to the caller.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(after, DirectoryTypeInfo);
        await _policy.WriteAllBytesAtomicAsync(path, bytes, cancellationToken).ConfigureAwait(false);

        lock (_cacheGate)
        {
            if (!_stores.TryGetValue(path, out var entry))
            {
                _stores[path] = new StoreEntry(after);
            }
            else
            {
                entry.Replace(after);
            }
        }
    }

    private SemaphoreSlim AcquireWriteLock(string path)
    {
        SemaphoreSlim gate;
        lock (_cacheGate)
        {
            if (!_writeLocks.TryGetValue(path, out gate!))
            {
                gate = new SemaphoreSlim(1, 1);
                _writeLocks[path] = gate;
            }
        }

        gate.Wait();
        return gate;
    }

    private static Dictionary<string, string?> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        return JsonSerializer.Deserialize(json, DirectoryTypeInfo)
            ?? new Dictionary<string, string?>(StringComparer.Ordinal);
    }

    private string ResolveReadPath(string baseName, string? requestPath)
    {
        if (!_policy.TryGetBaseDirectory(baseName, out var baseDir, out _))
        {
            throw new InvalidOperationException($"Base directory '{baseName}' is not available on this system.");
        }

        return _policy.Authorize(FileAccessKind.Read, baseDir, requestPath ?? string.Empty);
    }

    private string ResolveWritePath(string baseName, string? requestPath)
    {
        if (!_policy.TryGetBaseDirectory(baseName, out var baseDir, out var isReadOnly))
        {
            throw new InvalidOperationException($"Base directory '{baseName}' is not available on this system.");
        }

        if (isReadOnly)
        {
            throw new PathAccessDeniedException(PathDenialReason.OutsideBase,
                $"Base directory '{baseName}' is read-only.");
        }

        Directory.CreateDirectory(baseDir);
        return _policy.Authorize(FileAccessKind.Write, baseDir, requestPath ?? string.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_cacheGate)
        {
            foreach (var gate in _writeLocks.Values)
            {
                gate.Dispose();
            }
            _writeLocks.Clear();
        }
    }

    /// <summary>
    /// Mutable wrapper holding the latest committed snapshot. Writers replace the snapshot once the
    /// persist succeeds; readers capture it under the cache lock so concurrent replaces do not
    /// corrupt in-flight iterations.
    /// </summary>
    private sealed class StoreEntry
    {
        public StoreEntry(Dictionary<string, string?> snapshot)
        {
            Snapshot = snapshot;
        }

        public Dictionary<string, string?> Snapshot { get; private set; }

        public void Replace(Dictionary<string, string?> snapshot)
        {
            Snapshot = snapshot;
        }
    }
}
