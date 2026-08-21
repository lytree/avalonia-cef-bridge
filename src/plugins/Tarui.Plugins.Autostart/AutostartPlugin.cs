using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Autostart;

public sealed class AutostartPlugin(IAutostartService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            "plugin:autostart|is-enabled",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.AutostartState,
            (_, _, ct) => service.IsEnabledAsync(ct),
            "plugin:autostart|is-enabled");

        commands.Add(
            "plugin:autostart|enable",
            TaruiJsonContext.Default.AutostartEnableOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) =>
            {
                AutostartConfig.ValidateArgs(options.Args);
                return service.EnableAsync(options, ct);
            },
            "plugin:autostart|enable");

        commands.Add(
            "plugin:autostart|disable",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            (_, _, ct) => service.DisableAsync(ct),
            "plugin:autostart|disable");
    }
}

public static class AutostartPluginServiceCollectionExtensions
{
    public static IServiceCollection AddAutostartPlugin(this IServiceCollection services)
        => services.AddPlugin<AutostartPlugin>();
}