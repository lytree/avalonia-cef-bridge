using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Store;

/// <summary>
/// Default store service. Each distinct (Base, Path) resolves to a canonical file via
/// <c>IFileAccessPolicy</c> (rejecting rooted paths, link escapes, and read-only bases) and is loaded
/// once into an in-memory dictionary. Mutations apply in memory then persist durably through the
/// policy's atomic temporary-file replacement. Empty files and missing stores read as an empty
/// dictionary, so <c>get</c> is safe on first run.
/// </summary>
public sealed class JsonStoreService(IFileAccessPolicy policy) : IStoreService
{
    private static readonly JsonTypeInfo<Dictionary<string, string?>> DirectoryTypeInfo =
        (JsonTypeInfo<Dictionary<string, string?>>)StoreFileJsonContext.Default.GetTypeInfo(typeof(Dictionary<string, string?>))!;

    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, string?>> _cache = new(StringComparer.Ordinal);

    public ValueTask<StoreGetResult> GetAsync(StoreKeyOptions options, CancellationToken cancellationToken)
    {
        var store = GetStore(options.Base, options.Path, write: false);
        lock (_gate)
        {
            var value = store.TryGetValue(options.Key, out var current) ? current : null;
            return ValueTask.FromResult(new StoreGetResult(value));
        }
    }

    public async ValueTask<Unit> SetAsync(StoreSetOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveWritePath(options.Base, options.Path);
        lock (_gate)
        {
            var store = GetStoreLocked(path);
            if (options.Value is null)
            {
                store.Remove(options.Key);
            }
            else
            {
                store[options.Key] = options.Value;
            }
        }

        await PersistAsync(path, cancellationToken);
        return new Unit();
    }

    public ValueTask<StoreHasResult> HasAsync(StoreKeyOptions options, CancellationToken cancellationToken)
    {
        var store = GetStore(options.Base, options.Path, write: false);
        lock (_gate)
        {
            return ValueTask.FromResult(new StoreHasResult(store.ContainsKey(options.Key)));
        }
    }

    public async ValueTask<Unit> DeleteAsync(StoreKeyOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveWritePath(options.Base, options.Path);
        lock (_gate)
        {
            GetStoreLocked(path).Remove(options.Key);
        }

        await PersistAsync(path, cancellationToken);
        return new Unit();
    }

    public async ValueTask<Unit> ClearAsync(StoreFileOptions options, CancellationToken cancellationToken)
    {
        var path = ResolveWritePath(options.Base, options.Path);
        lock (_gate)
        {
            GetStoreLocked(path).Clear();
        }

        await PersistAsync(path, cancellationToken);
        return new Unit();
    }

    public ValueTask<StoreKeysResult> KeysAsync(StoreFileOptions options, CancellationToken cancellationToken)
    {
        var store = GetStore(options.Base, options.Path, write: false);
        string[] keys;
        lock (_gate)
        {
            keys = [.. store.Keys];
        }

        return ValueTask.FromResult(new StoreKeysResult(keys));
    }

    private Dictionary<string, string?> GetStore(string baseName, string? requestPath, bool write)
    {
        var path = write
            ? ResolveWritePath(baseName, requestPath)
            : ResolveReadPath(baseName, requestPath);

        lock (_gate)
        {
            return GetStoreLocked(path);
        }
    }

    private Dictionary<string, string?> GetStoreLocked(string path)
    {
        if (_cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var store = Load(path);
        _cache[path] = store;
        return store;
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

    private async Task PersistAsync(string path, CancellationToken cancellationToken)
    {
        Dictionary<string, string?> snapshot;
        lock (_gate)
        {
            snapshot = new Dictionary<string, string?>(_cache.TryGetValue(path, out var store) ? store : [], StringComparer.Ordinal);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, DirectoryTypeInfo);
        await policy.WriteAllBytesAtomicAsync(path, bytes, cancellationToken);
    }

    private string ResolveReadPath(string baseName, string? requestPath)
    {
        if (!policy.TryGetBaseDirectory(baseName, out var baseDir, out var isReadOnly))
        {
            throw new InvalidOperationException($"Base directory '{baseName}' is not available on this system.");
        }

        return policy.Authorize(FileAccessKind.Read, baseDir, requestPath ?? string.Empty);
    }

    private string ResolveWritePath(string baseName, string? requestPath)
    {
        if (!policy.TryGetBaseDirectory(baseName, out var baseDir, out var isReadOnly))
        {
            throw new InvalidOperationException($"Base directory '{baseName}' is not available on this system.");
        }

        if (isReadOnly)
        {
            throw new PathAccessDeniedException(PathDenialReason.OutsideBase,
                $"Base directory '{baseName}' is read-only.");
        }

        Directory.CreateDirectory(baseDir);
        return policy.Authorize(FileAccessKind.Write, baseDir, requestPath ?? string.Empty);
    }
}