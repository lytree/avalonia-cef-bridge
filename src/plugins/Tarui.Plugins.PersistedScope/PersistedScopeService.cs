using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.PersistedScope;

/// <summary>
/// Mutates the runtime scope overlay and queries it. Grants and resets are validated against the
/// fs/http allowlist (<see cref="PersistedScopePermissions"/>) and made durable before returning.
/// </summary>
public interface IPersistedScopeService
{
    /// <summary>Adds runtime allow entries for the permission, lifting matching deny entries.</summary>
    ValueTask<Unit> AllowAsync(PersistedScopeOptions options, CancellationToken ct);

    /// <summary>Adds runtime deny entries for the permission, removing matching allow entries.</summary>
    ValueTask<Unit> DenyAsync(PersistedScopeOptions options, CancellationToken ct);

    /// <summary>Removes one permission's overlay entry, or every entry when <see cref="PersistedScopeResetOptions.Permission"/> is null.</summary>
    ValueTask<Unit> ResetAsync(PersistedScopeResetOptions options, CancellationToken ct);
}

/// <summary>
/// Runtime scope entries for one structured permission. Lists are treated as immutable snapshots:
/// mutations build new lists and replace the whole state, so lock-free readers of the latest
/// snapshot never observe in-flight edits.
/// </summary>
public sealed class ScopeState(IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny)
{
    public static readonly ScopeState Empty = new([], []);

    public IReadOnlyList<PathScope> Allow { get; } = allow;

    public IReadOnlyList<PathScope> Deny { get; } = deny;
}

/// <summary>
/// Default persisted-scope service. Holds one runtime allow/deny overlay per structured permission
/// and persists it to <c>{appData}/scope.json</c> through the injected <see cref="IFileAccessPolicy"/>
/// with atomic writes. State is lazily loaded on first access (a missing, empty, or corrupt file
/// yields an empty state) and cached as an immutable snapshot. Every mutation runs under a single
/// write gate, persists first, and only then swaps the snapshot, so a failed persist leaves the
/// in-memory state untouched — mirroring the <c>JsonStoreService</c> discipline.
/// </summary>
public sealed class PersistedScopeService : IPersistedScopeService, IRuntimeScopeOverlay, IDisposable
{
    private const string FileName = "scope.json";

    private readonly IFileAccessPolicy _policy;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private IReadOnlyDictionary<string, ScopeState>? _state;
    private bool _loaded;
    private bool _disposed;

    public PersistedScopeService(IFileAccessPolicy policy) => _policy = policy;

    public ValueTask<Unit> AllowAsync(PersistedScopeOptions options, CancellationToken ct) =>
        GrantAsync(options.Permission, options.Paths, addAllow: true, ct);

    public ValueTask<Unit> DenyAsync(PersistedScopeOptions options, CancellationToken ct) =>
        GrantAsync(options.Permission, options.Paths, addAllow: false, ct);

    public async ValueTask<Unit> ResetAsync(PersistedScopeResetOptions options, CancellationToken ct)
    {
        if (options.Permission is not null)
        {
            PersistedScopePermissions.EnsureGrantable(options.Permission);
        }

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var before = Snapshot();
            Dictionary<string, ScopeState> after;
            if (options.Permission is null)
            {
                if (before.Count == 0)
                {
                    return new Unit();
                }

                after = new Dictionary<string, ScopeState>(StringComparer.Ordinal);
            }
            else
            {
                if (!before.ContainsKey(options.Permission))
                {
                    return new Unit();
                }

                after = new Dictionary<string, ScopeState>(before, StringComparer.Ordinal);
                after.Remove(options.Permission);
            }

            await CommitAsync(after, ct).ConfigureAwait(false);
            return new Unit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public bool TryGetOverlay(string permission, out IReadOnlyList<PathScope> allow, out IReadOnlyList<PathScope> deny)
    {
        var snapshot = Snapshot();
        if (snapshot.TryGetValue(permission, out var state))
        {
            allow = state.Allow;
            deny = state.Deny;
            return true;
        }

        allow = [];
        deny = [];
        return false;
    }

    private async ValueTask<Unit> GrantAsync(
        string permission,
        IReadOnlyList<PathScope> paths,
        bool addAllow,
        CancellationToken cancellationToken)
    {
        PersistedScopePermissions.EnsureGrantable(permission);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = Snapshot();
            var current = before.TryGetValue(permission, out var state) ? state : ScopeState.Empty;
            var allow = new List<PathScope>(current.Allow);
            var deny = new List<PathScope>(current.Deny);
            var removeFrom = addAllow ? deny : allow;
            var addTo = addAllow ? allow : deny;
            var changed = false;
            foreach (var path in paths)
            {
                // A grant replaces the opposite ruling: allowing a denied path lifts the deny and
                // vice versa, mirroring Tauri's persisted-scope semantics.
                if (removeFrom.RemoveAll(candidate => candidate == path) > 0)
                {
                    changed = true;
                }

                if (!addTo.Contains(path))
                {
                    addTo.Add(path);
                    changed = true;
                }
            }

            if (!changed)
            {
                return new Unit();
            }

            var after = new Dictionary<string, ScopeState>(before, StringComparer.Ordinal)
            {
                [permission] = new ScopeState(allow, deny),
            };
            await CommitAsync(after, cancellationToken).ConfigureAwait(false);
            return new Unit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private IReadOnlyDictionary<string, ScopeState> Snapshot()
    {
        lock (_stateGate)
        {
            if (!_loaded)
            {
                _state = Load();
                _loaded = true;
            }

            return _state!;
        }
    }

    private Dictionary<string, ScopeState> Load()
    {
        var empty = new Dictionary<string, ScopeState>(StringComparer.Ordinal);
        if (!_policy.TryGetBaseDirectory("appData", out var baseDirectory, out _))
        {
            return empty;
        }

        var path = Path.Combine(baseDirectory, FileName);
        if (!File.Exists(path))
        {
            return empty;
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return empty;
        }

        PersistedScopeFile? file;
        try
        {
            file = JsonSerializer.Deserialize(json, TaruiJsonContext.Default.PersistedScopeFile);
        }
        catch (JsonException)
        {
            // A corrupt file must never take the whole plugin down: start from an empty overlay and
            // let the next mutation rewrite the file from scratch.
            return empty;
        }

        if (file?.Scopes is null)
        {
            return empty;
        }

        var state = new Dictionary<string, ScopeState>(StringComparer.Ordinal);
        foreach (var (permission, entry) in file.Scopes)
        {
            state[permission] = new ScopeState([.. entry.Allow], [.. entry.Deny]);
        }

        return state;
    }

    private async Task CommitAsync(IReadOnlyDictionary<string, ScopeState> after, CancellationToken cancellationToken)
    {
        var path = ResolveWritePath();
        var file = new PersistedScopeFile(
            Version: 1,
            Scopes: after.ToDictionary(
                pair => pair.Key,
                pair => new PersistedScopeState(pair.Value.Allow, pair.Value.Deny),
                StringComparer.Ordinal));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(file, TaruiJsonContext.Default.PersistedScopeFile);

        // Persist first; only swap the in-memory snapshot once the file is durable, so a failed
        // persist leaves the cache on the previous snapshot and the exception reaches the caller.
        await _policy.WriteAllBytesAtomicAsync(path, bytes, cancellationToken).ConfigureAwait(false);

        lock (_stateGate)
        {
            _state = after;
        }
    }

    private string ResolveWritePath()
    {
        if (!_policy.TryGetBaseDirectory("appData", out var baseDirectory, out var isReadOnly))
        {
            throw new InvalidOperationException("The appData base directory is not available on this system.");
        }

        if (isReadOnly)
        {
            throw new PathAccessDeniedException(PathDenialReason.OutsideBase,
                "The appData base directory is read-only.");
        }

        Directory.CreateDirectory(baseDirectory);
        return _policy.Authorize(FileAccessKind.Write, baseDirectory, FileName);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeGate.Dispose();
    }
}
