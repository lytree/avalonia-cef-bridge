namespace Tarui.Contracts;

/// <summary>
/// Request for <c>plugin:persisted-scope|scope-allow</c> / <c>scope-deny</c>: adds runtime
/// allow/deny <see cref="PathScope"/> entries for a structured permission. Only fs and http
/// permissions accept runtime entries (see the plugin's permission allowlist); a runtime allow
/// entry removes a matching deny entry and vice versa.
/// </summary>
public sealed record PersistedScopeOptions(string Permission, IReadOnlyList<PathScope> Paths);

/// <summary>
/// Request for <c>plugin:persisted-scope|scope-reset</c>. A <see langword="null"/>
/// <see cref="Permission"/> clears every runtime scope entry; otherwise only that permission's
/// entry is removed.
/// </summary>
public sealed record PersistedScopeResetOptions(string? Permission = null);

/// <summary>
/// Payload of <c>scope://changed</c>. <see cref="Action"/> is <c>allow</c>, <c>deny</c>, or
/// <c>reset</c>; a full reset reports <c>*</c> as the permission.
/// </summary>
public sealed record PersistedScopeChangedEvent(string Permission, string Action);

/// <summary>On-disk shape of the persisted scope file (<c>{appData}/scope.json</c>).</summary>
public sealed record PersistedScopeFile(int Version, IReadOnlyDictionary<string, PersistedScopeState> Scopes);

/// <summary>Runtime allow/deny entries persisted for one structured permission.</summary>
public sealed record PersistedScopeState(IReadOnlyList<PathScope> Allow, IReadOnlyList<PathScope> Deny);
