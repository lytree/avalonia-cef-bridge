using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Menu;

public sealed class MenuPlugin(IMenuService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new MenuCommands(service);

        commands.Add(
            "plugin:menu|set-window-menu",
            TaruiJsonContext.Default.SetWindowMenuOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetWindowMenuAsync,
            "plugin:menu|set-window-menu");

        commands.Add(
            "plugin:menu|update-item",
            TaruiJsonContext.Default.MenuUpdateItemOptions,
            TaruiJsonContext.Default.Unit,
            handlers.UpdateItemAsync,
            "plugin:menu|update-item");

        commands.Add(
            "plugin:menu|remove-window-menu",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            handlers.RemoveWindowMenuAsync,
            "plugin:menu|remove-window-menu");
    }

    private sealed class MenuCommands(IMenuService service)
    {
        [TaruiCommand("plugin:menu|set-window-menu")]
        public ValueTask<Unit> SetWindowMenuAsync(
            SetWindowMenuOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetWindowMenuAsync(context.WindowLabel, options, cancellationToken);

        [TaruiCommand("plugin:menu|update-item")]
        public ValueTask<Unit> UpdateItemAsync(
            MenuUpdateItemOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.UpdateItemAsync(context.WindowLabel, options, cancellationToken);

        [TaruiCommand("plugin:menu|remove-window-menu")]
        public ValueTask<Unit> RemoveWindowMenuAsync(
            EmptyArgs options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.RemoveWindowMenuAsync(context.WindowLabel, cancellationToken);
    }
}

public static class MenuPluginServiceCollectionExtensions
{
    public static IServiceCollection AddMenuPlugin(this IServiceCollection services)
        => services.AddPlugin<MenuPlugin>();
}