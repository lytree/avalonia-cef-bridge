using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.DeepLink;

public sealed class DeepLinkPlugin(IDeepLinkService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            "plugin:deep-link|get-current",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.DeepLinkCurrentResult,
            (_, _, ct) => service.GetCurrentAsync(ct),
            "plugin:deep-link|get-current");

        commands.Add(
            "plugin:deep-link|feed",
            TaruiJsonContext.Default.DeepLinkFeedOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) => service.FeedAsync(options, ct),
            "plugin:deep-link|feed");
    }
}

public static class DeepLinkPluginServiceCollectionExtensions
{
    public static IServiceCollection AddDeepLinkPlugin(this IServiceCollection services)
        => services.AddPlugin<DeepLinkPlugin>();
}