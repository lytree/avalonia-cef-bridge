using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Plugins.Notification;

namespace Tarui.Shell.Toast;

public static class ToastServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WindowsToastNotificationService"/> as the <c>INotificationService</c>,
    /// replacing the previously registered backend (last registration wins). Must run after the
    /// base shell registration so the previous backend stays resolvable as the balloon fallback:
    /// <paramref name="fallbackFactory"/> resolves it explicitly; when omitted, the first
    /// registered notification service that is not the toast service itself is used. Hosts that
    /// resolve the non-Windows asset of this assembly get identical dedup semantics with every
    /// operation transparently degraded to the fallback.
    /// </summary>
    public static IServiceCollection AddWindowsToastNotifications(
        this IServiceCollection services,
        Func<IServiceProvider, INotificationService>? fallbackFactory = null)
        => services.AddSingleton<INotificationService>(sp => new WindowsToastNotificationService(
            ToastConfigurationReader.Read(sp.GetService<IConfiguration>()),
            sp.GetRequiredService<INotificationEventSink>(),
            fallbackFactory is not null
                ? fallbackFactory(sp)
                : sp.GetServices<INotificationService>()
                    .First(service => service is not WindowsToastNotificationService)));
}
