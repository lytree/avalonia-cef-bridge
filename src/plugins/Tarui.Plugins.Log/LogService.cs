using Microsoft.Extensions.Logging;
using Tarui.Contracts;

namespace Tarui.Plugins.Log;

/// <summary>
/// Default <see cref="ILogService"/>. A renderer record is promoted to a strongly-typed
/// <c>LogLevel</c> (unknown levels degrade to Information) and written to the category named by
/// <see cref="LogRecordOptions.Target"/> (or <c>renderer</c> when absent), joining the desktop
/// pipeline. The optional client timestamp is surfaced through structured scopes for correlation.
/// </summary>
public sealed class LogService(ILoggerFactory loggerFactory) : ILogService
{
    public ValueTask<Unit> RecordAsync(LogRecordOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var logger = loggerFactory.CreateLogger(string.IsNullOrEmpty(options.Target) ? "renderer" : options.Target);
        var level = ParseLevel(options.Level);
        var message = options.Message;

        // The renderer message is arbitrary text, not a logging template, so log it through the
        // raw generic overload to avoid CA2254/CA1848 (never interpret user text as a template).
        if (options.TimestampMs is long timestamp)
        {
            using var scope = logger.BeginScope(new[] { new KeyValuePair<string, object>("TaruiTimestamp", timestamp) });
            logger.Log(level, 0, message, null, (_, _) => message);
        }
        else
        {
            logger.Log(level, 0, message, null, (_, _) => message);
        }

        return ValueTask.FromResult(new Unit());
    }

    internal static LogLevel ParseLevel(string level)
        => Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed) ? parsed : LogLevel.Information;
}