using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Shell;

public sealed class EventRouter(IWindowSinkRegistry registry, EventHub hub)
{
    public IDisposable Subscribe<T>(string eventName, Action<T> handler) =>
        hub.Subscribe(eventName, handler);

    public async ValueTask EmitToWindowAsync(
        string label,
        string eventName,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        hub.Emit(eventName, payload);
        if (registry.TryGetSink(label, out var sink))
        {
            await sink.SendEventAsync(eventName, payload, cancellationToken);
        }
    }

    public async ValueTask EmitToAllAsync(
        string eventName,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        hub.Emit(eventName, payload);
        foreach (var label in registry.Labels)
        {
            if (registry.TryGetSink(label, out var sink))
            {
                await sink.SendEventAsync(eventName, payload, cancellationToken);
            }
        }
    }

    public ValueTask EmitAsync(
        string eventName,
        JsonElement payload,
        string? targetWindow,
        CancellationToken cancellationToken = default) =>
        targetWindow is null
            ? EmitToAllAsync(eventName, payload, cancellationToken)
            : EmitToWindowAsync(targetWindow, eventName, payload, cancellationToken);
}
