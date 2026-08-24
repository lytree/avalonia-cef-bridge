using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Shell;

/// <summary>
/// Shared guard for tray-icon paths. Tray commands accept icon identifiers in either
/// <c>base:relative/path</c> (a known base prefix plus a relative path) or a rooted absolute
/// path. The guard rejects UNC shares outright, follows any symlink/reparse point back to its
/// real target, and verifies the resulting physical path matches one of the supplied allow
/// scope <see cref="PathScope"/> entries.
/// </summary>
public static class TrayPathGuard
{
    /// <summary>
    /// Returns the default permissive scope used when the tray command has not been
    /// configured with an explicit allow list (matching the legacy behaviour: any
    /// resolved-within-an-authorized-base path is acceptable as long as UNC and symlink
    /// escapes are blocked).
    /// </summary>
    public static IReadOnlyList<PathScope> DefaultAllow() { return [new PathScope()]; }

    /// <summary>
    /// Resolves <paramref name="path"/> via the standard tray rules and throws
    /// <see cref="PathAccessDeniedException"/> when the resolved file is a UNC share, sits
    /// outside every allow scope, or escapes its base via a symlink. Returns the absolute
    /// filesystem path on success.
    /// </summary>
    public static string EnsureTrayIconAuthorized(
        string path,
        IReadOnlyList<PathScope> allowScopes,
        IReadOnlyList<PathScope> denyScopes)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new PathAccessDeniedException(PathDenialReason.IllegalSegment,
                "The tray icon path is empty.");
        }

        if (path.StartsWith('\\')
            || path.StartsWith('/'))
        {
            throw new PathAccessDeniedException(PathDenialReason.DeviceOrUnc,
                "UNC shares are not allowed as tray icon paths.");
        }

        var resolved = TrayIconPath.Resolve(path);

        // Walk the resolved path back to its real location so a symlinked icon cannot hop out
        // of the authorized base. The real path equality check below picks that up.
        var physical = ResolveRealPath(resolved);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!physical.Equals(resolved, comparison))
        {
            throw new PathAccessDeniedException(PathDenialReason.LinkEscape,
                "A symbolic link or reparse point escapes the authorized tray icon path.");
        }

        if (!MatchesAbsolutePath(allowScopes, physical))
        {
            throw new PathAccessDeniedException(PathDenialReason.OutsideBase,
                "The tray icon path is not covered by an allow scope.");
        }

        if (MatchesAbsolutePath(denyScopes, physical))
        {
            throw new PathAccessDeniedException(PathDenialReason.OutsideBase,
                "The tray icon path is denied by an explicit scope rule.");
        }

        return resolved;
    }

    /// <summary>
    /// Whether the supplied allow/deny list (interpreted as glob rules over absolute paths,
    /// independent of any <see cref="PathScope.Base"/> filter) covers <paramref name="absolute"/>.
    /// </summary>
    private static bool MatchesAbsolutePath(IReadOnlyList<PathScope> scopes, string absolute)
    {
        foreach (var scope in scopes)
        {
            if (string.IsNullOrEmpty(scope.Path))
            {
                return true;
            }

            if (FileScopeMatcher.MatchGlob(scope.Path, absolute))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveRealPath(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }

        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }

        return path;
    }
}
