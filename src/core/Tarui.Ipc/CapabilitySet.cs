using System.Collections.Frozen;
using Tarui.Contracts;

namespace Tarui.Ipc;

/// <summary>allow/deny path rules attached to a structured permission.</summary>
public sealed record PermissionScope(
    IReadOnlyList<PathScope> Allow,
    IReadOnlyList<PathScope> Deny)
{
    /// <summary>deny rules win over allow rules; a missing deny means "not denied".</summary>
    public bool Denies(PathScope entry) => Deny.Contains(entry);

    public bool AllowsPath(PathScope entry) => Allow.Contains(entry);
}

/// <summary>
/// The authorized surface of a single window: permission IDs, event names it may receive,
/// and allow/deny scopes attached to structured permissions.
/// </summary>
public sealed class CapabilitySet
{
    private readonly FrozenSet<string> _permissions;
    private readonly FrozenSet<string> _events;
    private readonly FrozenDictionary<string, PermissionScope> _scopes;

    public CapabilitySet(IEnumerable<string> permissions)
        : this(permissions, events: [], scopedPermissions: [])
    {
    }

    public CapabilitySet(
        IEnumerable<string> permissions,
        IEnumerable<string> events,
        IEnumerable<KeyValuePair<string, PermissionScope>> scopedPermissions)
    {
        _permissions = permissions.ToFrozenSet(StringComparer.Ordinal);
        _events = events.ToFrozenSet(StringComparer.Ordinal);
        _scopes = scopedPermissions.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public bool Allows(string permission) =>
        _permissions.Contains(permission) || _permissions.Contains("*");

    public bool AllowsEvent(string eventName) =>
        _events.Contains(eventName) || _events.Contains("*");

    public bool TryGetScope(string permission, out PermissionScope scope) =>
        _scopes.TryGetValue(permission, out scope!);

    public IReadOnlyCollection<string> Permissions => _permissions;

    public IReadOnlyCollection<string> Events => _events;

    public bool HasScopes => _scopes.Count > 0;

    /// <summary>
    /// Unions two capability sets into one: a surface holds a permission or event when either side
    /// grants it, and a scope union keeps every allow and every deny rule from both sides (denies
    /// never disappear through a merge, so unioning cannot widen a scoped restriction away).
    /// </summary>
    public CapabilitySet Merge(CapabilitySet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (_permissions.Contains("*") && _events.Contains("*") && _scopes.Count == 0)
        {
            return this;
        }

        if (other._permissions.Contains("*") && other._events.Contains("*") && other._scopes.Count == 0)
        {
            return other;
        }

        var scopes = new Dictionary<string, PermissionScope>(StringComparer.Ordinal);
        foreach (var pair in _scopes)
        {
            scopes[pair.Key] = pair.Value;
        }

        foreach (var pair in other._scopes)
        {
            scopes[pair.Key] = scopes.TryGetValue(pair.Key, out var existing)
                ? new PermissionScope(
                    existing.Allow.Concat(pair.Value.Allow).Distinct().ToArray(),
                    existing.Deny.Concat(pair.Value.Deny).Distinct().ToArray())
                : pair.Value;
        }

        return new CapabilitySet(
            _permissions.Concat(other._permissions),
            _events.Concat(other._events),
            scopes);
    }
}