namespace Tarui.Contracts;

/// <summary>
/// OS-level notification permission states. <c>granted</c> means the app may show notifications;
/// <c>denied</c> means the OS/system has blocked them; <c>prompt</c> means the user has not yet
/// decided. A platform that cannot surface notifications reports <see cref="NotificationPermissionStateResult.Supported"/> false.
/// </summary>
public static class NotificationPermissionState
{
    public const string Granted = "granted";
    public const string Denied = "denied";
    public const string Prompt = "prompt";
}

/// <summary>
/// Options for <c>plugin:notification|show</c>. <see cref="Id"/> is app-defined and used to cancel
/// the notification and to correlate <c>notification://activated</c>/<c>notification://dismissed</c>
/// events. <see cref="Icon"/> is an optional file path resolved against the well-known <c>base:</c>
/// identifiers; <see cref="Sound"/> requests an audible alert when the platform honours it.
/// </summary>
public sealed record NotificationOptions(
    string Id,
    string Title,
    string Body,
    string? Icon = null,
    bool Sound = false);

/// <summary>
/// Result of a permission query/request. <see cref="Supported"/> is false when the running platform
/// has no notification facility; <see cref="Reason"/> then carries a precise reason rather than
/// pretending success.
/// </summary>
public sealed record NotificationPermissionStateResult(
    string Permission,
    bool Supported = true,
    string? Reason = null);

public sealed record NotificationCancelOptions(string Id);

/// <summary>Payload of the <c>notification://activated</c> / <c>notification://dismissed</c> events.</summary>
public sealed record NotificationEvent(string Id, string? Title = null, string? Body = null, string? Action = null);