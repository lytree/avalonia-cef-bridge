using System.Text.Json;
using Tarui.Contracts;
using Tarui.Plugins.Notification;

namespace Tarui.Shell;

/// <summary>
/// Adapts toast lifecycle callbacks raised by the notification backend into reserved
/// <c>notification://activated</c>/<c>notification://dismissed</c> shell events. Delivery goes
/// through <see cref="EventRouter"/> so per-window capability event gating applies unchanged.
/// </summary>
public sealed class NotificationEventBridge(EventRouter router) : INotificationEventSink
{
    private const string ActivatedEvent = "notification://activated";
    private const string DismissedEvent = "notification://dismissed";

    public void DispatchActivated(NotificationEvent notificationEvent)
        => Emit(ActivatedEvent, notificationEvent);

    public void DispatchDismissed(NotificationEvent notificationEvent)
        => Emit(DismissedEvent, notificationEvent);

    private void Emit(string eventName, NotificationEvent notificationEvent)
    {
        // Toast callbacks arrive on WinRT threadpool threads; web delivery is async and
        // best-effort — a dead sink must never take down the notification pipeline.
        _ = Task.Run(async () =>
        {
            try
            {
                await router.EmitToAllAsync(
                    eventName,
                    JsonSerializer.SerializeToElement(notificationEvent, TaruiJsonContext.Default.NotificationEvent));
            }
            catch (Exception)
            {
                // Best-effort delivery; nothing sensible to report without a logger dependency.
            }
        });
    }
}
