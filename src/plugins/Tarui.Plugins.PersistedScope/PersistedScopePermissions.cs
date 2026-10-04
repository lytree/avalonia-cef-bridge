using System.Collections.Frozen;

namespace Tarui.Plugins.PersistedScope;

/// <summary>
/// Allowlist of structured permissions whose scopes the web layer may extend at runtime. Mirrors
/// Tauri's persisted-scope alignment: runtime entries only ever affect fs and http command scopes,
/// so a compromised renderer cannot loosen high-risk scopes such as <c>plugin:shell|spawn</c> by
/// calling the persisted-scope commands.
/// </summary>
public static class PersistedScopePermissions
{
    /// <summary>Permissions that accept runtime allow/deny scope entries.</summary>
    public static readonly FrozenSet<string> Grantable = new[]
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
        "plugin:fs|watch",
        "plugin:http|fetch",
        "plugin:http|upload",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Throws when <paramref name="permission"/> is outside the fs/http allowlist.</summary>
    public static void EnsureGrantable(string permission)
    {
        if (!Grantable.Contains(permission))
        {
            throw new InvalidOperationException(
                $"The persisted scope overlay may only extend fs and http permissions; '{permission}' is not grantable at runtime.");
        }
    }
}
