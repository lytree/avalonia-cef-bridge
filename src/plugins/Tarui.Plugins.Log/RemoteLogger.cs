using Microsoft.Extensions.Logging;
using Tarui.Contracts;

namespace Tarui.Plugins.Log;

/// <summary>
/// Receives structured log entries produced by <see cref="RemoteLoggerProvider"/>. The shell supplies
/// the concrete sink, which forwards entries to authorized windows on <c>log://entry</c> via the event
/// router; the plugin keeps this abstraction so the provider stays platform- and wiring-agnostic.
/// </summary>
public interface IRemoteLogSink
{
    void Publish(LogEntry entry);
}

/// <summary>
/// A <see cref="ILoggerProvider"/> that bridges <c>Microsoft.Extensions.Logging</c> to the renderer.
/// Registered as a DI <see cref="ILoggerProvider"/> singleton, it is enumerated by the shared
/// <c>LoggerFactory</c> and streams every enabled file/console message into <see cref="IRemoteLogSink"/>.
/// </summary>
public sealed class RemoteLoggerProvider(IRemoteLogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RemoteLogger(sink, categoryName);

    public void Dispose()
    {
    }
}

/// <summary>
/// Formats a single log statement and forwards it to the sink. Scopes are not propagated because the
/// wire event is a flat, UI-friendly <see cref="LogEntry"/>; the message is the formatted <c>state</c>,
/// with any exception appended for diagnostics.
/// </summary>
public sealed class RemoteLogger(IRemoteLogSink sink, string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (exception is not null)
        {
            message = string.Concat(message, Environment.NewLine, exception);
        }

        sink.Publish(new LogEntry(
            logLevel.ToString(),
            message,
            categoryName,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }
}