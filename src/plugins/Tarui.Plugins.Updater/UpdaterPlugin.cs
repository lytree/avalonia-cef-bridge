using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tarui.Plugins.Events;
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

        commands.Add(
            "plugin:updater|apply",
            TaruiJsonContext.Default.UpdateApplyOptions,
            TaruiJsonContext.Default.UpdateApplyResult,
            (options, _, ct) => service.ApplyAsync(options, ct),
            "plugin:updater|apply");

        commands.Add(
            "plugin:updater|restore",
            TaruiJsonContext.Default.UpdateRestoreOptions,
            TaruiJsonContext.Default.UpdateRestoreResult,
            (options, _, ct) => service.RestoreAsync(options, ct),
            "plugin:updater|restore");

        commands.Add(
            "plugin:updater|verify",
            TaruiJsonContext.Default.UpdateVerifyOptions,
            TaruiJsonContext.Default.UpdateVerifyResult,
            (options, _, ct) => service.VerifyAsync(options, ct),
            "plugin:updater|verify");
    }
}

public static class UpdaterPluginServiceCollectionExtensions
{
    /// <summary>
    /// Registers the updater command surface together with the services it needs to run: the HTTP
    /// client used to fetch signed manifests and packages, the platform update applier and the
    /// service itself. Registering this plugin is sufficient — nothing is left to the shell.
    /// </summary>
    public static IServiceCollection AddUpdaterPlugin(this IServiceCollection services)
    {
        // Shared when another package (for example the HTTP plugin host) already registered one.
        services.TryAddSingleton<HttpClient>();
        return services
            .AddPlugin<UpdaterPlugin>()
            .AddSingleton<IUpdateApplier>(static _ => OperatingSystem.IsWindows()
                ? new WindowsMsixUpdateApplier()
                : new NoOpUpdateApplier())
            .AddSingleton<IUpdaterService>(static sp => new UpdaterService(
                sp.GetRequiredService<HttpClient>(),
                UpdaterConfiguration.ReadSettings(sp.GetService<IConfiguration>()),
                sp.GetService<IEventSender>(),
                sp.GetService<ILogger<UpdaterService>>() ?? NullLogger<UpdaterService>.Instance,
                sp.GetRequiredService<IUpdateApplier>()));
    }
}
