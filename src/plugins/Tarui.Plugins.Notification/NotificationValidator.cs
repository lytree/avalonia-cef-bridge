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
    public const int MaxActionsPerNotification = 5;
    public const int MaxActionTextLength = 64;

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

        if (options.Actions is not null)
        {
            if (options.Actions.Count > MaxActionsPerNotification)
            {
                throw new InvalidPayloadException();
            }

            foreach (var action in options.Actions)
            {
                if (string.IsNullOrWhiteSpace(action.Id) || action.Id.Length > MaxActionTextLength
                    || string.IsNullOrWhiteSpace(action.Label) || action.Label.Length > MaxActionTextLength)
                {
                    throw new InvalidPayloadException();
                }
            }
        }
    }
}