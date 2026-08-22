namespace Tarui.Cli;

/// <summary>Shared manifest loading, validation and path resolution used by dev/build/info.</summary>
internal static class ManifestLoader
{
    /// <summary>Loads and validates the manifest, throwing a single fatal error on any issue.</summary>
    public static AppManifest LoadValidated(CliPaths paths)
    {
        AppManifest manifest;
        try
        {
            manifest = AppManifestLoader.Load(paths.ManifestPath);
        }
        catch (CliException exception)
        {
            throw new CliException($"Failed to load manifest: {exception.Message}");
        }

        var errors = AppManifestValidator.Validate(manifest, paths.ManifestDirectory);
        if (errors.Count > 0)
        {
            throw new CliException(
                $"Invalid tarui.app.json:{Environment.NewLine}  - " +
                string.Join(Environment.NewLine + "  - ", errors));
        }

        return manifest;
    }

    /// <summary>Resolves the dev server URL used by <c>tarui dev</c>.</summary>
    public static Uri ResolveDevUrl(AppManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Build.DevUrl))
        {
            throw new CliException("build.devUrl is required for 'tarui dev'.");
        }

        if (!Uri.TryCreate(manifest.Build.DevUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new CliException($"build.devUrl must be an absolute http(s) URL, got '{manifest.Build.DevUrl}'.");
        }

        return uri;
    }

    /// <summary>Resolves the desktop .csproj path from the option override or the manifest.</summary>
    public static string ResolveDesktopProject(AppManifest manifest, string? overridePath, CliPaths paths)
    {
        var relative = overridePath ?? manifest.Build.DesktopProject;
        if (string.IsNullOrWhiteSpace(relative))
        {
            throw new CliException(
                "No desktop project configured. Set build.desktopProject in tarui.app.json or pass --project.");
        }

        var resolved = paths.ResolveRelative(relative);
        if (!File.Exists(resolved))
        {
            throw new CliException($"Desktop project not found: {resolved}");
        }

        return resolved;
    }
}
