using System.Text.RegularExpressions;

namespace Tarui.Cli;

/// <summary>
/// Rewrites a scaffolded desktop .csproj from published NuGet packages to
/// <c>ProjectReference</c> entries pointing at a local Tarui source tree, and
/// points the CEF/web content sources at that tree. Used by <c>tarui init --local</c>.
/// </summary>
internal static class LocalReferenceRewriter
{
    /// <summary>PackageId → repo-relative .csproj path.</summary>
    private static readonly Dictionary<string, string> RepositoryProjects =
        new(StringComparer.Ordinal)
        {
            ["Tarui.Hosting"] = "src/desktop/Tarui.Hosting/Tarui.Hosting.csproj",
            ["Tarui.Shell"] = "src/desktop/Tarui.Shell/Tarui.Shell.csproj",
            ["Tarui.SingleInstance"] = "src/desktop/Tarui.SingleInstance/Tarui.SingleInstance.csproj",
            ["Tarui.WebView.CefGlueNext"] = "src/webview/Tarui.WebView.CefGlueNext/Tarui.WebView.CefGlueNext.csproj",
            ["Tarui.Plugins.Core"] = "src/plugins/Tarui.Plugins.Core/Tarui.Plugins.Core.csproj",
            ["Tarui.Plugins.Window"] = "src/plugins/Tarui.Plugins.Window/Tarui.Plugins.Window.csproj",
            ["Tarui.Ipc"] = "src/core/Tarui.Ipc/Tarui.Ipc.csproj",
            ["Tarui.Contracts"] = "src/core/Tarui.Contracts/Tarui.Contracts.csproj",
            ["Tarui.Ipc.Generators"] = "src/generators/Tarui.Ipc.Generators/Tarui.Ipc.Generators.csproj",
        };

    private static readonly Regex PackageReferencePattern = new(
        @"<PackageReference\s+Include=""(?<id>[^""]+)""\s+(?<attrs>(?:[^>]*?)\s*Version=""[^""]+""[^>]*?)\s*/>",
        RegexOptions.Compiled);

    /// <summary>Returns the repo-relative project path for a package, or null if it is not an in-repo Tarui package.</summary>
    public static string? ResolveProjectReference(string packageId) =>
        RepositoryProjects.TryGetValue(packageId, out var relative) ? relative : null;

    public static void RewriteFile(string csprojPath, string repoRoot)
    {
        var original = File.ReadAllText(csprojPath);
        var rewritten = RewriteContent(original, repoRoot);
        File.WriteAllText(csprojPath, rewritten);
    }

    /// <summary>Pure transform of the .csproj content. Testable without touching disk.</summary>
    public static string RewriteContent(string csprojContent, string repoRoot)
    {
        var repo = Path.GetFullPath(repoRoot).Replace('\\', '/').TrimEnd('/');

        // Replace only the <PackageReference> element, leaving indentation and the rest of
        // the file byte-for-byte intact (including newlines).
        var replacements = 0;
        var content = PackageReferencePattern.Replace(
            csprojContent,
            match =>
            {
                var resolved = ResolveProjectReference(match.Groups["id"].Value);
                if (resolved is null)
                {
                    return match.Value;
                }

                replacements++;
                // Preserve analyzer/metadata attributes (e.g. Tarui.Ipc.Generators) so the
                // local project reference behaves identically to the published package.
                var attrs = match.Groups["attrs"].Value.Trim();
                // Strip the trailing Version="..." attribute: versions do not apply to project references.
                var keep = Regex.Replace(attrs, @"\s*Version=""[^""]*""", string.Empty, RegexOptions.Compiled);
                var suffix = string.IsNullOrWhiteSpace(keep) ? string.Empty : " " + keep.Trim();
                return "<ProjectReference Include=\"" + repo + "/" + ToUrlPath(resolved) + "\"" + suffix + " />";
            });

        // Point the CEF/web content sources at the local source tree. These
        // override the template defaults because MSBuild uses last-write-wins.
        var cefRoot = $"{repo}/runtime/cef";
        var webDist = $"{repo}/web/apps/Tarui.Web/dist";
        var drop = "</Project>";
        content = Regex.Replace(content, drop,
            $"  <PropertyGroup>\n" +
            $"    <TaruiCefRuntimeRoot>{cefRoot}</TaruiCefRuntimeRoot>\n" +
            $"    <TaruiWebDistRoot>{webDist}</TaruiWebDistRoot>\n" +
            $"  </PropertyGroup>\n</Project>");

        if (replacements == 0 && !content.Contains(cefRoot, StringComparison.Ordinal))
        {
            throw new CliException(
                "The desktop project had no in-repo Tarui packages to rewrite. Is it the scaffolded project?");
        }

        return content;
    }

    private static string ToUrlPath(string path) => path.Replace('\\', '/');
}