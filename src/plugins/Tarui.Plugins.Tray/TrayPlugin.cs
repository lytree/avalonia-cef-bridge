using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Tray;

public sealed class TrayPlugin(ITrayService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new TrayCommands(service);

        commands.Add(
            "plugin:tray|create",
            TaruiJsonContext.Default.TrayCreateOptions,
            TaruiJsonContext.Default.Unit,
            handlers.CreateAsync,
            "plugin:tray|create");

        commands.Add(
            "plugin:tray|set-menu",
            TaruiJsonContext.Default.TraySetMenuOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetMenuAsync,
            "plugin:tray|set-menu");

        commands.Add(
            "plugin:tray|set-icon",
            TaruiJsonContext.Default.TraySetIconOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetIconAsync,
            "plugin:tray|set-icon");

        commands.Add(
            "plugin:tray|set-tooltip",
            TaruiJsonContext.Default.TraySetTooltipOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetTooltipAsync,
            "plugin:tray|set-tooltip");

        commands.Add(
            "plugin:tray|set-visible",
            TaruiJsonContext.Default.TraySetVisibleOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetVisibleAsync,
            "plugin:tray|set-visible");

        commands.Add(
            "plugin:tray|remove",
            TaruiJsonContext.Default.TrayRemoveOptions,
            TaruiJsonContext.Default.Unit,
            handlers.RemoveAsync,
            "plugin:tray|remove");
    }

    private sealed class TrayCommands(ITrayService service)
    {
        [TaruiCommand("plugin:tray|create")]
        public ValueTask<Unit> CreateAsync(
            TrayCreateOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.CreateAsync(context.WindowLabel, options, cancellationToken);

        [TaruiCommand("plugin:tray|set-menu")]
        public ValueTask<Unit> SetMenuAsync(
            TraySetMenuOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetMenuAsync(context.WindowLabel, options, cancellationToken);

        [TaruiCommand("plugin:tray|set-icon")]
        public ValueTask<Unit> SetIconAsync(
            TraySetIconOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetIconAsync(context.WindowLabel, options, cancellationToken);

        [TaruiCommand("plugin:tray|set-tooltip")]
        public ValueTask<Unit> SetTooltipAsync(
            TraySetTooltipOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetTooltipAsync(context.WindowLabel, options, cancellationToken);

        [TaruiCommand("plugin:tray|set-visible")]
        public ValueTask<Unit> SetVisibleAsync(
            TraySetVisibleOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetVisibleAsync(context.WindowLabel, options, cancellationToken);

        [TaruiCommand("plugin:tray|remove")]
        public ValueTask<Unit> RemoveAsync(
            TrayRemoveOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.RemoveAsync(context.WindowLabel, options, cancellationToken);
    }
}

public static class TrayPluginServiceCollectionExtensions
{
    public static IServiceCollection AddTrayPlugin(this IServiceCollection services)
        => services.AddPlugin<TrayPlugin>();
}