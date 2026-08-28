using Microsoft.Extensions.Logging;

namespace Tarui.Ipc;

internal static partial class FireAndForgetLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Fire-and-forget notification failed.")]
    internal static partial void NotificationFailed(this ILogger logger, Exception exception);
}
