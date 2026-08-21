namespace Tarui.Contracts;

/// <summary>
/// A scoped path rule used by structured permissions. <see cref="Base"/> names a known
/// directory base (for example <c>appData</c>, <c>temp</c>, <c>resources</c>) and
/// <see cref="Path"/> is a glob-relative path below that base.
/// </summary>
public sealed record PathScope(
    string? Base = null,
    string? Path = null);

/// <summary>
/// A single permission entry inside a capability manifest. A plain string permission is
/// represented with only <see cref="Identifier"/> set; structured permissions also carry
/// allow/deny <see cref="PathScope"/> lists.
/// </summary>
public sealed record CapabilityGrant(
    string Identifier,
    PathScope[]? Allow = null,
    PathScope[]? Deny = null);

/// <summary>
/// The parsed shape of a Tarui desktop capability manifest file. String fields are optional
/// to tolerate the original flat format while remaining strict about what is validated.
/// </summary>
public sealed record CapabilityManifest(
    string Identifier,
    CapabilityGrant[] Permissions,
    string? Description = null,
    string[]? Windows = null,
    string[]? Platforms = null,
    string[]? Events = null);