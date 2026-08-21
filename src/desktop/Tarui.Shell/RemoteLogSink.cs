using System.Text.Json;
using System.Threading.Channels;
using Tarui.Contracts;
using Tarui.Plugins.Log;

namespace Tarui.Shell;

/// <summary>
/// Bridges <see cref="IRemoteLogSink"/> to the web view. Entries are queued on an unbounded channel
/// and drained on a background task into the reserved <c>log://entry</c> event, which the event router
/// delivers only to windows whose capability declares receive authorization. Logging never blocks the
/// producing thread, and delivery is best-effort (a failed send is dropped, not re-thrown).
/// </summary>
public sealed class RemoteLogSink : IRemoteLogSink, IDisposable
{
    private readonly EventRouter _events;
    private readonly Channel<LogEntry> _channel = Channel.CreateUnbounded<LogEntry>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Task _pump;
    private bool _disposed;

    public RemoteLogSink(EventRouter events)
    {
        _events = events;
        _pump = Task.Run(DrainAsync);
    }

    public void Publish(LogEntry entry) => _channel.Writer.TryWrite(entry);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        try
        {
            _channel.Reader.TryRead(out _);
        }
        catch
        {
            // Best effort.
        }
    }

    private async Task DrainAsync()
    {
        await foreach (var entry in _channel.Reader.ReadAllAsync())
        {
            try
            {
                await _events.EmitToAllAsync(
                    LogEventNames.Entry,
                    JsonSerializer.SerializeToElement(entry, TaruiJsonContext.Default.LogEntry));
            }
            catch
            {
                // Remote log delivery is best-effort.
            }
        }
    }
}