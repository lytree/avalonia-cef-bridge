using Tarui.Contracts;

namespace Tarui.Plugins.Notification;

/// <summary>
/// System notification operations. Permission state and delivery are platform-defined; a platform
/// with no notification facility reports <see cref="NotificationPermissionStateResult.Supported"/>
/// false with a precise <see cref="NotificationPermissionStateResult.Reason"/> instead of faking success.
/// </summary>
public interface INotificationService
{
    ValueTask<NotificationPermissionStateResult> GetPermissionStateAsync(CancellationToken cancellationToken);

    ValueTask<NotificationPermissionStateResult> RequestPermissionAsync(CancellationToken cancellationToken);

    ValueTask<Unit> ShowAsync(NotificationOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> CancelAsync(NotificationCancelOptions options, CancellationToken cancellationToken);
}