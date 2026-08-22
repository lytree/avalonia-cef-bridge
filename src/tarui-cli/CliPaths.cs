namespace Tarui.Cli;

/// <summary>
/// Resolves the manifest path and interprets manifest-relative paths against the
/// manifest directory (Tauri semantics: relative paths are relative to the app dir).
/// </summary>
internal sealed record CliPaths(string ManifestPath, string ManifestDirectory)
{
    public static CliPaths Resolve(string? manifestPath)
    {
        var resolved = manifestPath ?? Path.Combine(Environment.CurrentDirectory, "tarui.app.json");
        var full = Path.GetFullPath(resolved);
        var directory = Path.GetDirectoryName(full)
            ?? throw new CliException($"Cannot determine the directory of manifest '{full}'.");
        return new CliPaths(full, directory);
    }

    /// <summary>Resolves a manifest-relative path to an absolute path.</summary>
    public string ResolveRelative(string? path) =>
        Path.GetFullPath(Path.Combine(ManifestDirectory, path ?? string.Empty));

    /// <summary>
    /// Working directory for before-dev/build commands: the configured frontend
    /// workspace root when present, otherwise the manifest directory.
    /// </summary>
    public string FrontendWorkingDirectory(AppManifestBuild build) =>
        string.IsNullOrWhiteSpace(build.Frontend)
            ? ManifestDirectory
            : ResolveRelative(build.Frontend);
}
