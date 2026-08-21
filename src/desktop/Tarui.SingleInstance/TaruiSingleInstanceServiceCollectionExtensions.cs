using Microsoft.Extensions.DependencyInjection;
using Tarui.Ipc;

namespace Tarui.SingleInstance;

public static class TaruiSingleInstanceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the primary-instance channel coordinator and its host lifecycle. Must only be
    /// called after <see cref="SingleInstanceGuard.Acquire"/> reported <see cref="InstanceRole.Primary"/>;
    /// a secondary process forwards its arguments and exits before the host is ever built.
    /// </summary>
    public static IServiceCollection AddSingleInstance(
        this IServiceCollection services,
        SingleInstanceIdentity identity)
    {
        services.AddSingleton(identity);
        services.AddSingleton<SingleInstanceCoordinator>();
        services.AddSingleton<ISingleInstanceCoordinator>(sp => sp.GetRequiredService<SingleInstanceCoordinator>());
        services.AddSingleton<SingleInstanceHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<SingleInstanceHostedService>());
        return services;
    }
}