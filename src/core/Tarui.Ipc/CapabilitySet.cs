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
}