using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.FileSystem;

public interface IFileSystemService
{
    ValueTask<FsReadTextResult> ReadTextAsync(FsPathOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> WriteTextAsync(FsWriteTextOptions options, CancellationToken cancellationToken);

    ValueTask<FsDirEntry[]> ReadDirAsync(FsReadDirOptions options, CancellationToken cancellationToken);

    ValueTask<FsStatResult?> StatAsync(FsPathOptions options, CancellationToken cancellationToken);

    ValueTask<bool> ExistsAsync(FsPathOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> MkdirAsync(FsMkdirOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> CopyAsync(FsCopyOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> RenameAsync(FsRenameOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> RemoveAsync(FsRemoveOptions options, CancellationToken cancellationToken);
}

public sealed class FileSystemPlugin(IFileSystemService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new FileSystemCommands(service);

        commands.Add(
            "plugin:fs|read-text-file",
            TaruiJsonContext.Default.FsPathOptions,
            TaruiJsonContext.Default.FsReadTextResult,
            handlers.ReadTextAsync,
            "plugin:fs|read-text-file",
            FsScopeAuthorizer.AllowsPath);

        commands.Add(
            "plugin:fs|write-text-file",
            TaruiJsonContext.Default.FsWriteTextOptions,
            TaruiJsonContext.Default.Unit,
            handlers.WriteTextAsync,
            "plugin:fs|write-text-file",
            FsScopeAuthorizer.AllowsPathWrite);

        commands.Add(
            "plugin:fs|read-dir",
            TaruiJsonContext.Default.FsReadDirOptions,
            TaruiJsonContext.Default.FsDirEntryArray,
            handlers.ReadDirAsync,
            "plugin:fs|read-dir",
            FsScopeAuthorizer.AllowsPath);

        commands.Add(
            "plugin:fs|stat",
            TaruiJsonContext.Default.FsPathOptions,
            TaruiJsonContext.Default.FsStatResult,
            handlers.StatAsync,
            "plugin:fs|stat",
            FsScopeAuthorizer.AllowsPath);

        commands.Add(
            "plugin:fs|exists",
            TaruiJsonContext.Default.FsPathOptions,
            TaruiJsonContext.Default.Boolean,
            handlers.ExistsAsync,
            "plugin:fs|exists",
            FsScopeAuthorizer.AllowsPath);

        commands.Add(
            "plugin:fs|mkdir",
            TaruiJsonContext.Default.FsMkdirOptions,
            TaruiJsonContext.Default.Unit,
            handlers.MkdirAsync,
            "plugin:fs|mkdir",
            FsScopeAuthorizer.AllowsPathWrite);

        commands.Add(
            "plugin:fs|copy-file",
            TaruiJsonContext.Default.FsCopyOptions,
            TaruiJsonContext.Default.Unit,
            handlers.CopyAsync,
            "plugin:fs|copy-file",
            FsScopeAuthorizer.AllowsFromToWrite);

        commands.Add(
            "plugin:fs|rename",
            TaruiJsonContext.Default.FsRenameOptions,
            TaruiJsonContext.Default.Unit,
            handlers.RenameAsync,
            "plugin:fs|rename",
            FsScopeAuthorizer.AllowsFromToWrite);

        commands.Add(
            "plugin:fs|remove",
            TaruiJsonContext.Default.FsRemoveOptions,
            TaruiJsonContext.Default.Unit,
            handlers.RemoveAsync,
            "plugin:fs|remove",
            FsScopeAuthorizer.AllowsPathWrite);
    }

    private sealed class FileSystemCommands(IFileSystemService service)
    {
        [TaruiCommand("plugin:fs|read-text-file")]
        public ValueTask<FsReadTextResult> ReadTextAsync(
            FsPathOptions options,
            CommandContext context,
            CancellationToken cancellationToken) => service.ReadTextAsync(options, cancellationToken);

        [TaruiCommand("plugin:fs|write-text-file")]
        public ValueTask<Unit> WriteTextAsync(
            FsWriteTextOptions options,
            CommandContext context,
            CancellationToken cancellationToken) => service.WriteTextAsync(options, cancellationToken);

        [TaruiCommand("plugin:fs|read-dir")]
        public async ValueTask<FsDirEntry[]> ReadDirAsync(
            FsReadDirOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            var entries = await service.ReadDirAsync(options, cancellationToken);
            return [.. entries];
        }

        [TaruiCommand("plugin:fs|stat")]
        public ValueTask<FsStatResult?> StatAsync(
            FsPathOptions options,
            CommandContext context,
            CancellationToken cancellationToken) => service.StatAsync(options, cancellationToken);

        [TaruiCommand("plugin:fs|exists")]
        public ValueTask<bool> ExistsAsync(
            FsPathOptions options,
            CommandContext context,
            CancellationToken cancellationToken) => service.ExistsAsync(options, cancellationToken);

        [TaruiCommand("plugin:fs|mkdir")]
        public ValueTask<Unit> MkdirAsync(
            FsMkdirOptions options,
            CommandContext context,
            CancellationToken cancellationToken) => service.MkdirAsync(options, cancellationToken);

        [TaruiCommand("plugin:fs|copy-file")]
        public ValueTask<Unit> CopyAsync(
            FsCopyOptions options,
            CommandContext context,
            CancellationToken cancellationToken) => service.CopyAsync(options, cancellationToken);

        [TaruiCommand("plugin:fs|rename")]
        public ValueTask<Unit> RenameAsync(
            FsRenameOptions options,
            CommandContext context,
            CancellationToken cancellationToken) => service.RenameAsync(options, cancellationToken);

        [TaruiCommand("plugin:fs|remove")]
        public ValueTask<Unit> RemoveAsync(
            FsRemoveOptions options,
            CommandContext context,
            CancellationToken cancellationToken) => service.RemoveAsync(options, cancellationToken);
    }
}

/// <summary>
/// File system commands authorize the (Base, Path) pair against the caller capability's
/// allow/deny <c>PathScope</c> lists, with the shared <see cref="FileScopeMatcher"/> so deny
/// patterns cannot be bypassed via a different casing on Windows. Deny wins over allow; an
/// empty allow means "any (base, path) not explicitly denied". Write-family commands
/// additionally reject the <c>resources</c> base.
/// </summary>
public static class FsScopeAuthorizer
{
    public static bool AllowsPath(FsPathOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path);

    public static bool AllowsPath(FsReadDirOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path);

    public static bool AllowsPathWrite(FsWriteTextOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.Base) && FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path);

    public static bool AllowsPathWrite(FsMkdirOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.Base) && FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path);

    public static bool AllowsPathWrite(FsRemoveOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.Base) && FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path);

    public static bool AllowsFromToWrite(FsCopyOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.ToBase) &&
        FileScopeMatcher.MatchesScope(allow, deny, options.FromBase, options.FromPath) &&
        FileScopeMatcher.MatchesScope(allow, deny, options.ToBase, options.ToPath);

    public static bool AllowsFromToWrite(FsRenameOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.ToBase) &&
        FileScopeMatcher.MatchesScope(allow, deny, options.FromBase, options.FromPath) &&
        FileScopeMatcher.MatchesScope(allow, deny, options.ToBase, options.ToPath);

    private static bool IsReadOnlyBase(string? baseName) =>
        string.Equals(baseName, "resources", StringComparison.OrdinalIgnoreCase);
}

public static class FileSystemPluginServiceCollectionExtensions
{
    public static IServiceCollection AddFileSystemPlugin(this IServiceCollection services) => services
        .AddFileAccessPolicy()
        .AddSingleton<IFileSystemService, FileSystemService>()
        .AddPlugin<FileSystemPlugin>();
}
