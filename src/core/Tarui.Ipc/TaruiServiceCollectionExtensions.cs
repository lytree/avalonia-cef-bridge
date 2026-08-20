using Microsoft.Extensions.DependencyInjection;

namespace Tarui.Ipc;

public static class TaruiServiceCollectionExtensions
{
    public static IServiceCollection AddPlugin<TPlugin>(this IServiceCollection services)
        where TPlugin : class, ITaruiPlugin
        => services.AddSingleton<ITaruiPlugin, TPlugin>();
}
