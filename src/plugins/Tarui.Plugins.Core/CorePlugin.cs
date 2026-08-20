using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Core;

public sealed class CorePlugin : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            "core:app|get-info",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.AppHandshake,
            static (_, context, cancellationToken) => GetInfoAsync(context, cancellationToken),
            "core:app|get-info");
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
}

public static class CorePluginServiceCollectionExtensions
{
    public static IServiceCollection AddCorePlugin(this IServiceCollection services)
        => services.AddPlugin<CorePlugin>();
}
