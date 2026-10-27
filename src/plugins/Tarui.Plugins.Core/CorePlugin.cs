using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Core;

public sealed class CorePlugin(IAppShutdown shutdown) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new CoreCommands(shutdown);

        commands.Add(
            "core:app|get-info",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.AppHandshake,
            static (_, context, cancellationToken) => GetInfoAsync(context, cancellationToken),
            "core:app|get-info");

        // Tauri 2.12 promoted `app.exit` out of the process plugin so an application can stop
        // without registering it. The command still routes through IAppShutdown so the host stops
        // cooperatively instead of the renderer killing the process.
        commands.Add(
            "core:app|exit",
            TaruiJsonContext.Default.ProcessExitOptions,
            TaruiJsonContext.Default.Unit,
            handlers.ExitAsync,
            "core:app|exit");
    }

    [TaruiCommand("core:app|get-info")]
    private static ValueTask<AppHandshake> GetInfoAsync(
        CommandContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AppHandshake(
            "tarui.net",
            "0.1.0",
            1,
            Environment.OSVersion.Platform.ToString(),
            [.. context.Capabilities.Permissions]));
    }

    private sealed class CoreCommands(IAppShutdown shutdown)
    {
        [TaruiCommand("core:app|exit")]
        public ValueTask<Unit> ExitAsync(
            ProcessExitOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            shutdown.RequestShutdown(options.Code);
            return ValueTask.FromResult(new Unit());
        }
    }
}

public static class CorePluginServiceCollectionExtensions
{
    public static IServiceCollection AddCorePlugin(this IServiceCollection services)
    {
        // The composition root swaps in the host-backed implementation; a no-op keeps the plugin
        // resolvable in minimal applications and tests that never start a host.
        services.TryAddSingleton<IAppShutdown, NoopAppShutdown>();
        return services.AddPlugin<CorePlugin>();
    }
}
