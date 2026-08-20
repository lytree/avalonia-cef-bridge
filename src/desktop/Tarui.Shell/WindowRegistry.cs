using Avalonia.Controls;
using System.Text.Json;
using Tarui.Ipc;

namespace Tarui.Shell;

public interface IEventSink
{
    ValueTask SendEventAsync(
        string eventName,
        JsonElement payload,
        CancellationToken cancellationToken = default);
}

public interface IWindowSinkRegistry
{
    IReadOnlyCollection<string> Labels { get; }

    bool TryGetSink(string label, out IEventSink sink);
}

public sealed class WindowRegistry : IWindowSinkRegistry
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public sealed record Entry(Window Window, IEventSink Sink, CommandContext Context)
    {
        internal bool ClosePending { get; set; }
    }

    public IReadOnlyCollection<string> Labels
    {
        get
        {
            lock (_gate)
            {
                return _entries.Keys.ToArray();
            }
        }
    }

    public void Add(string label, Entry entry)
    {
        lock (_gate)
        {
            if (!_entries.TryAdd(label, entry))
            {
                throw new InvalidOperationException($"A window with label '{label}' already exists.");
            }
        }
    }

    public Entry Get(string label)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(label, out var entry))
            {
                return entry;
            }
        }

        throw new KeyNotFoundException($"No window is registered with label '{label}'.");
    }

    public bool TryGet(string label, out Entry entry)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(label, out entry!);
        }
    }

    public bool Remove(string label)
    {
        lock (_gate)
        {
            return _entries.Remove(label);
        }
    }

    public bool TryGetSink(string label, out IEventSink sink)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(label, out var entry))
            {
                sink = entry.Sink;
                return true;
            }
        }

        sink = null!;
        return false;
    }
}
