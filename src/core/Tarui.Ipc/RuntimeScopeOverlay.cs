using Tarui.Contracts;

namespace Tarui.Ipc;

/// <summary>
/// Runtime-added allow/deny <see cref="PathScope"/> entries for structured permissions, persisted
/// across restarts by the persisted-scope plugin. The overlay only ever extends scopes a capability
/// manifest already declared; it never introduces checks for permissions that declared none.
/// </summary>
public interface IRuntimeScopeOverlay
{
    /// <summary>Try to fetch runtime-added allow/deny entries for a structured permission.</summary>
    bool TryGetOverlay(string permission, out IReadOnlyList<PathScope> allow, out IReadOnlyList<PathScope> deny);
}
