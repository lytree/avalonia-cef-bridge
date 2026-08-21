using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.GlobalShortcut;

public sealed class GlobalShortcutPlugin(IGlobalShortcutService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            "plugin:global-shortcut|register",
            TaruiJsonContext.Default.GlobalShortcutOptions,
            TaruiJsonContext.Default.GlobalShortcutState,
            (options, _, ct) => service.RegisterAsync(options, ct),
            "plugin:global-shortcut|register",
            AcceleratorScopeAuthorizer);

        commands.Add(
            "plugin:global-shortcut|unregister",
            TaruiJsonContext.Default.GlobalShortcutOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) => service.UnregisterAsync(options, ct),
            "plugin:global-shortcut|unregister",
            AcceleratorScopeAuthorizer);

        commands.Add(
            "plugin:global-shortcut|unregister-all",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            (_, _, ct) => service.UnregisterAllAsync(ct),
            "plugin:global-shortcut|unregister-all");

        commands.Add(
            "plugin:global-shortcut|is-registered",
            TaruiJsonContext.Default.GlobalShortcutOptions,
            TaruiJsonContext.Default.GlobalShortcutState,
            (options, _, ct) => service.IsRegisteredAsync(options, ct),
            "plugin:global-shortcut|is-registered",
            AcceleratorScopeAuthorizer);
    }

    /// <summary>
    /// A global shortcut is registrable on this window only when its accelerator matches the granted
    /// <c>allow</c> scope (or the scope lists a bare end-of-path pattern) and is not matched by any
    /// <c>deny</c>. Keeping this authorizer in the plugin makes the accelerator scope rules
    /// unit-testable and identical across platforms.
    /// </summary>
    private static bool AcceleratorScopeAuthorizer(
        GlobalShortcutOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny)
    {
        var spec = AcceleratorSpec.Parse(options.Accelerator);
        return !spec.Matches(deny) && (allow.Count == 0 || spec.Matches(allow));
    }
}

public static class GlobalShortcutPluginServiceCollectionExtensions
{
    public static IServiceCollection AddGlobalShortcutPlugin(this IServiceCollection services)
        => services.AddPlugin<GlobalShortcutPlugin>();
}