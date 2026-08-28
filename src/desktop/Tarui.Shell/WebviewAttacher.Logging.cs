using Microsoft.Extensions.Logging;

namespace Tarui.Shell;

public sealed partial class WebviewAttacher
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Window '{Label}' did not confirm close within {Timeout}; forcing shutdown to keep the OS responsive.")]
    internal partial void LogCloseRequestTimeoutExceeded(string label, TimeSpan timeout);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Force-close of window '{Label}' threw; the OS will fall back to its own handler.")]
    internal partial void LogCloseRequestForceCloseFailed(Exception exception, string label);
}
