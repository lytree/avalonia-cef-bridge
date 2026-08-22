using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Updater;

public sealed class UpdaterPlugin(IUpdaterService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            "plugin:updater|check",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.UpdateCheckResult,
            (options, _, ct) => service.CheckAsync(options, ct),
            "plugin:updater|check");

        commands.Add(
            "plugin:updater|download",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.UpdateDownloadResult,
            (options, _, ct) => service.DownloadAsync(options, ct),
            "plugin:updater|download");
    }
}

public static class UpdaterPluginServiceCollectionExtensions
{
    public static IServiceCollection AddUpdaterPlugin(this IServiceCollection services)
        => services.AddPlugin<UpdaterPlugin>();
}