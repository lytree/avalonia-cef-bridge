using Microsoft.Extensions.DependencyInjection;

namespace Tarui.Ipc;

/// <summary>
/// Configuration for the application's native resource directory, mirroring Tauri v3's resource
/// resolution rework (T6): the packaged resource root is a first-class concept separate from the
/// executable directory, in development the resources are read straight from the source tree (so an
/// edit is visible without a rebuild), and resources declared as remapped are mirrored next to the
/// executable on first access.
/// <para>
/// Every value falls back to an environment variable when it is not set here, so a deployment can
/// override the packaged layout without changing code: <c>TARUI_RESOURCE_DIR</c>,
/// <c>TARUI_RESOURCE_DEV_DIR</c>, <c>TARUI_RESOURCE_MIRROR_DIR</c> and
/// <c>TARUI_RESOURCE_MIRRORED</c> (comma separated, relative paths).
/// </para>
/// </summary>
public sealed record ResourceDirectoryOptions
{
    /// <summary>
    /// The packaged resource root. When null, <c>TARUI_RESOURCE_DIR</c> is used, then
    /// <c>{BaseDirectory}/resources</c> when it exists, then <c>{BaseDirectory}</c>.
    /// </summary>
    public string? Root { get; init; }

    /// <summary>
    /// The source-tree resource root used while developing. When set and present, it takes precedence
    /// over <see cref="Root"/> so resource edits are live. Null disables the development override.
    /// </summary>
    public string? DevelopmentRoot { get; init; }

    /// <summary>
    /// The directory remapped resources are mirrored to. Null disables mirroring; a typical value is
    /// the executable directory.
    /// </summary>
    public string? MirrorDirectory { get; init; }

    /// <summary>Relative paths of the resources declared as remapped and therefore mirrored.</summary>
    public IReadOnlyList<string> MirroredResources { get; init; } = [];
}

/// <summary>
/// Resolves native application resources (<c>assets/icon.png</c>, seed data, templates, …) to real
/// files, independently of the web asset resolver. Requests are relative paths authorized by
/// <see cref="IFileAccessPolicy"/> against the <c>resources</c> base, so the file system plugin and
/// the resource directory share one containment rule instead of growing a second resource boundary.
/// </summary>
public interface ITaruiResourceDirectory
{
    /// <summary>The effective resource root (the development root while developing).</summary>
    string Root { get; }

    /// <summary>The configured development root, or null when resources come from the packaged root.</summary>
    string? DevelopmentRoot { get; }

    /// <summary>Whether <see cref="DevelopmentRoot"/> is configured and present.</summary>
    bool IsDevelopment { get; }

    /// <summary>
    /// Resolves <paramref name="relativePath"/> to an existing file inside the resource root.
    /// Returns null when the path is malformed, escapes the root, or does not exist.
    /// </summary>
    string? TryResolve(string relativePath);

    /// <summary>
    /// Mirrors the configured remapped resources next to the executable. Idempotent: the first call
    /// copies what is missing or stale, later calls are no-ops. A file that is up to date is left
    /// untouched, so a restart does not rewrite every resource.
    /// </summary>
    ValueTask MirrorRemappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ITaruiResourceDirectory"/>. Paths are validated by the shared
/// <see cref="IFileAccessPolicy"/> (lexical containment plus link-escape checks); mirroring is a
/// host-side copy using a temporary file plus rename, never a user-triggered file operation.
/// </summary>
public sealed class ResourceDirectory : ITaruiResourceDirectory
{
    private readonly IFileAccessPolicy _policy;
    private readonly string? _mirrorDirectory;
    private readonly IReadOnlyList<string> _mirroredResources;
    private readonly object _mirrorGate = new();
    private bool _mirrored;

    public ResourceDirectory(ResourceDirectoryOptions? options = null, IFileAccessPolicy? policy = null)
    {
        options ??= new ResourceDirectoryOptions();

        DevelopmentRoot = ResolveDevelopmentRoot(options);
        IsDevelopment = DevelopmentRoot is not null && Directory.Exists(DevelopmentRoot);

        // In development the source tree is the resource root, so an edit is visible immediately and
        // no copy step can go stale. Otherwise the packaged root wins.
        Root = IsDevelopment ? DevelopmentRoot! : ResolvePackagedRoot(options);

        var mirrorDirectory = options.MirrorDirectory
            ?? Environment.GetEnvironmentVariable("TARUI_RESOURCE_MIRROR_DIR");
        _mirrorDirectory = string.IsNullOrWhiteSpace(mirrorDirectory)
            ? null
            : Path.GetFullPath(mirrorDirectory);

        _mirroredResources = options.MirroredResources.Count > 0
            ? options.MirroredResources
            : ReadMirroredResourcesFromEnvironment();

        // The file system policy's read-only "resources" base resolves to the same root, so plugin
        // calls and resource lookups share one resource boundary.
        _policy = policy ?? new FileAccessPolicy(
            new FileAccessPolicyOptions { ResourcesDirectory = Root });
    }

    /// <inheritdoc />
    public string Root { get; }

    /// <inheritdoc />
    public string? DevelopmentRoot { get; }

    /// <inheritdoc />
    public bool IsDevelopment { get; }

    /// <inheritdoc />
    public string? TryResolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        string authorized;
        try
        {
            authorized = _policy.Authorize(FileAccessKind.Read, Root, relativePath);
        }
        catch (PathAccessDeniedException)
        {
            return null;
        }

        return File.Exists(authorized) ? authorized : null;
    }

    /// <inheritdoc />
    public async ValueTask MirrorRemappedAsync(CancellationToken cancellationToken = default)
    {
        if (_mirrorDirectory is null || _mirroredResources.Count == 0)
        {
            return;
        }

        lock (_mirrorGate)
        {
            if (_mirrored)
            {
                return;
            }

            _mirrored = true;
        }

        foreach (var relativePath in _mirroredResources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = TryResolve(relativePath);
            if (source is null)
            {
                continue;
            }

            var target = ResolveMirrorTarget(relativePath);
            if (target is null || IsUpToDate(source, target))
            {
                continue;
            }

            await CopyAtomicallyAsync(source, target, cancellationToken);
        }
    }

    private string? ResolveMirrorTarget(string relativePath)
    {
        var mirrorRoot = _mirrorDirectory!;
        var candidate = Path.GetFullPath(Path.Combine(mirrorRoot, relativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = mirrorRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison) ? candidate : null;
    }

    private static bool IsUpToDate(string source, string target)
    {
        var sourceInfo = new FileInfo(source);
        var targetInfo = new FileInfo(target);
        return targetInfo.Exists
            && targetInfo.Length == sourceInfo.Length
            && targetInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc;
    }

    private static async Task CopyAtomicallyAsync(
        string source,
        string target,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>
    /// The resource root these options select: the development root while it exists, otherwise the
    /// packaged root. Shared with <see cref="FileAccessPolicy"/> so the two never disagree.
    /// </summary>
    internal static string ResolveRoot(ResourceDirectoryOptions options)
    {
        var development = ResolveDevelopmentRoot(options);
        return development is not null && Directory.Exists(development)
            ? development
            : ResolvePackagedRoot(options);
    }

    private static string? ResolveDevelopmentRoot(ResourceDirectoryOptions options)
    {
        var developmentRoot = options.DevelopmentRoot
            ?? Environment.GetEnvironmentVariable("TARUI_RESOURCE_DEV_DIR");
        return string.IsNullOrWhiteSpace(developmentRoot)
            ? null
            : Path.GetFullPath(developmentRoot);
    }

    private static string ResolvePackagedRoot(ResourceDirectoryOptions options)
    {
        var root = options.Root ?? Environment.GetEnvironmentVariable("TARUI_RESOURCE_DIR");
        return string.IsNullOrWhiteSpace(root) ? DefaultRoot() : Path.GetFullPath(root);
    }

    internal static string DefaultRoot()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "resources");
        return Directory.Exists(bundled) ? bundled : AppContext.BaseDirectory;
    }

    private static string[] ReadMirroredResourcesFromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("TARUI_RESOURCE_MIRRORED");
        return string.IsNullOrWhiteSpace(configured)
            ? []
            : configured.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

public static class ResourceDirectoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ResourceDirectoryOptions"/> and <see cref="ITaruiResourceDirectory"/>. The
    /// options are shared with <see cref="FileAccessPolicy"/> so the <c>resources</c> base and the
    /// resource directory cannot drift apart; register this before resolving the file system policy.
    /// </summary>
    public static IServiceCollection AddTaruiResourceDirectory(
        this IServiceCollection services,
        ResourceDirectoryOptions? options = null)
    {
        if (options is not null)
        {
            services.AddSingleton(options);
        }

        return services.AddSingleton<ITaruiResourceDirectory>(sp => new ResourceDirectory(
            sp.GetService<ResourceDirectoryOptions>(),
            sp.GetService<IFileAccessPolicy>()));
    }
}
