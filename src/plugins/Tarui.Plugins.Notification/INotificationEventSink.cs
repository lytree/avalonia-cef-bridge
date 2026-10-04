using Tarui.Contracts;

namespace Tarui.Plugins.Notification;

/// <summary>
/// Receives OS-level notification lifecycle callbacks (user activated a toast or its action
/// buttons, or dismissed it). Platform backends raise these; the shell adapts them into
/// <c>notification://activated</c>/<c>notification://dismissed</c> events for the Web side.
/// </summary>
public interface INotificationEventSink
{
    /// <summary>The user clicked the toast body (<see cref="NotificationEvent.Action"/> null) or one of its action buttons.</summary>
    void DispatchActivated(NotificationEvent notificationEvent);

    /// <summary>The user dismissed the notification or the OS expired it.</summary>
    void DispatchDismissed(NotificationEvent notificationEvent);
}
