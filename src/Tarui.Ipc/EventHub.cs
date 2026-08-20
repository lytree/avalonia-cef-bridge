using System.Collections.Concurrent;

namespace Tarui.Ipc;

public sealed class EventHub
{
    private readonly ConcurrentDictionary<string, List<Action<object?>>> _handlers = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public IDisposable Subscribe<T>(string eventName, Action<T> handler)
    {
        Action<object?> boxed = payload =>
        {
            if (payload is T value)
            {
                handler(value);
            }
        };

        lock (_gate)
        {
            var handlers = _handlers.GetOrAdd(eventName, static _ => []);
            handlers.Add(boxed);
        }

        return new Subscription(() =>
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(eventName, out var handlers))
                {
                    handlers.Remove(boxed);
                }
            }
        });
    }

    public void Emit<T>(string eventName, T payload)
    {
        Action<object?>[] handlers;
        lock (_gate)
        {
            handlers = _handlers.TryGetValue(eventName, out var registered)
                ? [.. registered]
                : [];
        }

        foreach (var handler in handlers)
        {
            handler(payload);
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
