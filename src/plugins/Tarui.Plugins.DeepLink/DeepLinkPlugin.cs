using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Tarui.Plugins.Events;
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
    /// <summary>
    /// Registers the deep link command surface, the URL aggregator and the hosted registrars that
    /// claim the configured schemes and file associations with the operating system.
    /// </summary>
    public static IServiceCollection AddDeepLinkPlugin(this IServiceCollection services) => services
        .AddPlugin<DeepLinkPlugin>()
        .AddSingleton(static sp => new DeepLinkService(
            GetStartupArgs(),
            DeepLinkConfiguration.ReadSchemes(sp.GetService<IConfiguration>()),
            sp.GetRequiredService<IEventSender>()))
        .AddSingleton<IDeepLinkService>(static sp => sp.GetRequiredService<DeepLinkService>())
        // A second instance forwards its command line through the single-instance channel; the deep
        // link aggregator is the component that knows how to interpret it.
        .AddSingleton<ISecondActivationSink>(static sp => sp.GetRequiredService<DeepLinkService>())
        .AddSingleton<IMacDeepLinkUrlExtractor, NoOpMacDeepLinkUrlExtractor>()
        .AddHostedService<DeepLinkRegistrarHostedService>()
        .AddHostedService<FileAssociationRegistrarHostedService>();

    private static string[] GetStartupArgs()
    {
        var args = Environment.GetCommandLineArgs();
        return args.Length > 1 ? args[1..] : [];
    }
}
