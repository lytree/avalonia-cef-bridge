using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Notification;

public sealed class NotificationPlugin(INotificationService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            "plugin:notification|permission-state",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.NotificationPermissionStateResult,
            (_, _, ct) => service.GetPermissionStateAsync(ct),
            "plugin:notification|permission-state");

        commands.Add(
            "plugin:notification|request-permission",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.NotificationPermissionStateResult,
            (_, _, ct) => service.RequestPermissionAsync(ct),
            "plugin:notification|request-permission");

        commands.Add(
            "plugin:notification|show",
            TaruiJsonContext.Default.NotificationOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) =>
            {
                NotificationValidator.Validate(options);
                return service.ShowAsync(options, ct);
            },
            "plugin:notification|show");

        commands.Add(
            "plugin:notification|cancel",
            TaruiJsonContext.Default.NotificationCancelOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) => service.CancelAsync(options, ct),
            "plugin:notification|cancel");
    }
}

public static class NotificationPluginServiceCollectionExtensions
{
    /// <summary>
    /// Registers the notification command surface, the platform backend and the event bridge that
    /// forwards activation/dismissal callbacks as reserved <c>notification://</c> events. A host that
    /// wants richer toasts can decorate <see cref="INotificationService"/> afterwards (the shell's
    /// toast package does exactly that).
    /// </summary>
    public static IServiceCollection AddNotificationPlugin(this IServiceCollection services) => services
        .AddPlugin<NotificationPlugin>()
        .AddSingleton<WindowsNotificationService>()
        .AddSingleton<INotificationService>(static sp => sp.GetRequiredService<WindowsNotificationService>())
        .AddSingleton<INotificationEventSink, NotificationEventBridge>();
}
