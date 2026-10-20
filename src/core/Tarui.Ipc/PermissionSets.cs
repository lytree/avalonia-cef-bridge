using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Tarui.Ipc;

/// <summary>
/// One curated group of permission ids a capability manifest may reference as a single entry.
/// This is the Tarui equivalent of a Tauri ACL permission set (<c>core:window|default</c>,
/// <c>plugin:sql|default</c>): the manifest names the group, the loader expands it into the concrete
/// permission ids the router registered, and no plugin has to hand-maintain a second list.
/// </summary>
public sealed record PermissionSetDefinition(
    string Identifier,
    IReadOnlyList<string> Permissions,
    string? Description = null);

/// <summary>
/// Resolves and expands <see cref="PermissionSetDefinition"/> entries while capability manifests load.
/// </summary>
public interface IPermissionSetCatalog
{
    /// <summary>Every declared set, in declaration order (curated sets first, materialized ones after).</summary>
    IReadOnlyList<PermissionSetDefinition> Sets { get; }

    bool TryGet(string identifier, [NotNullWhen(true)] out PermissionSetDefinition? definition);

    /// <summary>
    /// Expands <paramref name="identifier"/> into concrete permission ids. Nested set members are
    /// expanded recursively and duplicates are collapsed. Throws <see cref="InvalidOperationException"/>
    /// for an unknown set identifier or a cycle.
    /// </summary>
    IReadOnlyList<string> Expand(string identifier);
}

/// <summary>
/// Immutable catalog of permission sets. A set may reference other sets by identifier; expansion
/// rejects cycles instead of silently truncating them.
/// </summary>
public sealed class PermissionSetCatalog : IPermissionSetCatalog
{
    /// <summary>A catalog without any set: capability manifests then only carry literal permission ids.</summary>
    public static PermissionSetCatalog Empty { get; } = new([]);

    private readonly FrozenDictionary<string, PermissionSetDefinition> _sets;
    private readonly PermissionSetDefinition[] _ordered;

    public PermissionSetCatalog(IEnumerable<PermissionSetDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var map = new Dictionary<string, PermissionSetDefinition>(StringComparer.Ordinal);
        var ordered = new List<PermissionSetDefinition>();
        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Identifier))
            {
                throw new InvalidOperationException("A permission set identifier must be non-empty.");
            }

            if (definition.Permissions is null || definition.Permissions.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Permission set '{definition.Identifier}' must declare at least one permission.");
            }

            foreach (var permission in definition.Permissions)
            {
                if (string.IsNullOrWhiteSpace(permission))
                {
                    throw new InvalidOperationException(
                        $"Permission set '{definition.Identifier}' contains an empty member.");
                }
            }

            if (!map.TryAdd(definition.Identifier, definition))
            {
                throw new InvalidOperationException($"Duplicate permission set '{definition.Identifier}'.");
            }

            ordered.Add(definition);
        }

        _sets = map.ToFrozenDictionary(StringComparer.Ordinal);
        _ordered = [.. ordered];
    }

    public IReadOnlyList<PermissionSetDefinition> Sets => _ordered;

    public bool TryGet(string identifier, [NotNullWhen(true)] out PermissionSetDefinition? definition) =>
        _sets.TryGetValue(identifier, out definition);

    public IReadOnlyList<string> Expand(string identifier)
    {
        if (!_sets.ContainsKey(identifier))
        {
            throw new InvalidOperationException($"Unknown permission set '{identifier}'.");
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ExpandCore(identifier, result, seen, []);
        return result;
    }

    private void ExpandCore(string identifier, List<string> result, HashSet<string> seen, List<string> visiting)
    {
        if (!_sets.TryGetValue(identifier, out var definition))
        {
            // A non-set member is a literal permission id; the router composer validates it.
            if (seen.Add(identifier))
            {
                result.Add(identifier);
            }

            return;
        }

        if (visiting.Contains(identifier, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Permission set expansion cycle: {string.Join(" -> ", visiting)} -> {identifier}.");
        }

        visiting.Add(identifier);
        foreach (var member in definition.Permissions)
        {
            ExpandCore(member, result, seen, visiting);
        }

        visiting.RemoveAt(visiting.Count - 1);
    }
}

/// <summary>
/// Naming convention shared by the ACL surface: a permission id names its namespace by the part
/// before the last <c>|</c> (<c>plugin:fs|read-text-file</c> lives in <c>plugin:fs</c>), and a
/// permission with neither a <c>core:</c> nor a <c>plugin:</c> prefix belongs to the application
/// namespace <see cref="AppNamespace"/>. Every namespace materializes a <c>|default</c> set plus one
/// set per cross-target guard suffix, so capabilities never have to enumerate commands by hand.
/// </summary>
public static class PermissionSetNames
{
    /// <summary>Namespace used for application-defined commands that carry no plugin prefix.</summary>
    public const string AppNamespace = "app";

    /// <summary>The one set every application gets for free: all of its own commands.</summary>
    public const string AppDefaultSet = AppNamespace + "|default";

    public const string DefaultSuffix = "default";
    public const string OtherWindowSuffix = "other-window";
    public const string OtherWebviewSuffix = "other-webview";

    private const string CorePrefix = "core:";
    private const string PluginPrefix = "plugin:";
    private const string OtherWindowGuard = "-other-window";
    private const string OtherWebviewGuard = "-other-webview";

    /// <summary>Returns the namespace a permission id belongs to.</summary>
    public static string NamespaceOf(string permission)
    {
        var separator = permission.LastIndexOf('|');
        if (separator > 0)
        {
            return permission[..separator];
        }

        return permission.StartsWith(CorePrefix, StringComparison.Ordinal) ||
               permission.StartsWith(PluginPrefix, StringComparison.Ordinal)
            ? permission
            : AppNamespace;
    }

    /// <summary>Returns the <c>{namespace}|default</c> set id covering <paramref name="permission"/>.</summary>
    public static string DefaultSetOf(string permission) => NamespaceOf(permission) + "|" + DefaultSuffix;

    public static bool IsOtherWindowGuard(string permission) =>
        permission.EndsWith(OtherWindowGuard, StringComparison.Ordinal);

    public static bool IsOtherWebviewGuard(string permission) =>
        permission.EndsWith(OtherWebviewGuard, StringComparison.Ordinal);

    /// <summary>
    /// True for the <c>-other-window</c> / <c>-other-webview</c> guard permissions. Guards authorize
    /// reaching another window or web view, so they never belong to a <c>|default</c> set.
    /// </summary>
    public static bool IsCrossTargetGuard(string permission) =>
        IsOtherWindowGuard(permission) || IsOtherWebviewGuard(permission);

    /// <summary>Set id that groups the cross-target guards of <paramref name="permission"/>'s namespace.</summary>
    public static string CrossTargetSetOf(string permission)
    {
        var suffix = IsOtherWebviewGuard(permission) ? OtherWebviewSuffix : OtherWindowSuffix;
        return NamespaceOf(permission) + "|" + suffix;
    }
}
