using Tarui.Ipc;

namespace Tarui.Plugins.Notification;

/// <summary>
/// Pure, dependency-free validation for notification payloads. Kept in the plugin (not the shell)
/// so the id/title/body rules are unit-testable and identical on every platform.
/// </summary>
public static class NotificationValidator
{
    public const int MaxIdLength = 64;
    public const int MaxTitleLength = 128;
    public const int MaxBodyLength = 512;
    public const int MaxArgsPerNotification = 4;

    public static void Validate(Tarui.Contracts.NotificationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Id) || options.Id.Length > MaxIdLength)
        {
            throw new InvalidPayloadException();
        }

        if (string.IsNullOrWhiteSpace(options.Title) || options.Title.Length > MaxTitleLength)
        {
            throw new InvalidPayloadException();
        }

        if (options.Body is null || string.IsNullOrWhiteSpace(options.Body) || options.Body.Length > MaxBodyLength)
        {
            throw new InvalidPayloadException();
        }
    }
}