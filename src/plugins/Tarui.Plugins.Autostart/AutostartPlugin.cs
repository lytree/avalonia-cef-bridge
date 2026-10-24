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
    /// <summary>
    /// Registers the autostart command surface and the platform registration backend it drives.
    /// </summary>
    public static IServiceCollection AddAutostartPlugin(this IServiceCollection services) => services
        .AddPlugin<AutostartPlugin>()
        .AddSingleton<IAutostartService>(static _ =>
        {
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Autostart requires the application executable path.");
            if (OperatingSystem.IsMacOS())
            {
                return new MacAutostartService(MacAutostartService.DefaultBaseDirectory(), processPath);
            }

            return OperatingSystem.IsLinux()
                ? new LinuxAutostartService(LinuxAutostartService.DefaultBaseDirectory(), processPath)
                : new WindowsAutostartService();
        });
}
