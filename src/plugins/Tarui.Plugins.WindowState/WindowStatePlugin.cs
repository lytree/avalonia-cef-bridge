using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.WindowState;

public sealed class WindowStatePlugin(IWindowStateService service) : ITaruiPlugin
{
    private static readonly string[] OtherWindowCapablePermissions =
    [
        "plugin:window-state|save",
        "plugin:window-state|restore",
        "plugin:window-state|clear",
    ];

    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new WindowStateCommands(service);

        commands.Add(
            "plugin:window-state|save",
            TaruiJsonContext.Default.WindowStateSaveOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SaveAsync,
            "plugin:window-state|save");

        commands.Add(
            "plugin:window-state|restore",
            TaruiJsonContext.Default.WindowStateRestoreOptions,
            TaruiJsonContext.Default.WindowStateRestoreResult,
            handlers.RestoreAsync,
            "plugin:window-state|restore");

        commands.Add(
            "plugin:window-state|clear",
            TaruiJsonContext.Default.WindowStateClearOptions,
            TaruiJsonContext.Default.Unit,
            handlers.ClearAsync,
            "plugin:window-state|clear");

        // Cross-window state operations require the <permission>-other-window variant; register the
        // ids so capability files may reference them and validation stays strict.
        foreach (var permission in OtherWindowCapablePermissions)
        {
            commands.AddPermission(WindowStatePermissionGuard.OtherWindowPermission(permission));
        }
    }

    private sealed class WindowStateCommands(IWindowStateService service)
    {
        [TaruiCommand("plugin:window-state|save")]
        public ValueTask<Unit> SaveAsync(
            WindowStateSaveOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SaveAsync(Resolve(options.Label, context, "plugin:window-state|save"), cancellationToken);

        [TaruiCommand("plugin:window-state|restore")]
        public ValueTask<WindowStateRestoreResult> RestoreAsync(
            WindowStateRestoreOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.RestoreAsync(Resolve(options.Label, context, "plugin:window-state|restore"), cancellationToken);

        [TaruiCommand("plugin:window-state|clear")]
        public ValueTask<Unit> ClearAsync(
            WindowStateClearOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.ClearAsync(Resolve(options.Label, context, "plugin:window-state|clear"), cancellationToken);

        private static string Resolve(string? requested, CommandContext context, string permission)
        {
            var label = requested ?? context.WindowLabel;
            WindowStatePermissionGuard.EnsureOwnOrOtherWindow(context, label, permission);
            return label;
        }
    }
}

/// <summary>Enforces cross-window authorization for the window-state commands.</summary>
public static class WindowStatePermissionGuard
{
    public static string OtherWindowPermission(string permission) => permission + "-other-window";

    public static void EnsureOwnOrOtherWindow(CommandContext context, string targetLabel, string permission)
    {
        if (string.Equals(targetLabel, context.WindowLabel, StringComparison.Ordinal))
        {
            return;
        }

        var other = OtherWindowPermission(permission);
        if (!context.Capabilities.Allows(other))
        {
            throw new PermissionDeniedException(other);
        }
    }
}

public static class WindowStatePluginServiceCollectionExtensions
{
    public static IServiceCollection AddWindowStatePlugin(this IServiceCollection services)
        => services.AddPlugin<WindowStatePlugin>();
}