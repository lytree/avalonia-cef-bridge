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
        if (MayReceive(label, eventName) &&
            registry.TryGetSink(label, out var sink))
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
            if (MayReceive(label, eventName) &&
                registry.TryGetSink(label, out var sink))
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

    /// <summary>
    /// Delivers a <c>webview://</c> reserved event to a specific web view by its own label. A web view
    /// and its host window share one label while a window hosts a single surface, so the sink is resolved
    /// through the same entry; this channel is kept distinct from <see cref="EmitToWindowAsync"/> so that
    /// multi-webview layouts can address surfaces independently without changing the window channel.
    /// </summary>
    public ValueTask EmitToWebviewAsync(
        string webviewLabel,
        string eventName,
        JsonElement payload,
        CancellationToken cancellationToken = default) =>
        EmitToWindowAsync(webviewLabel, eventName, payload, cancellationToken);

    /// <summary>
    /// Reserved native events may carry sensitive system data (window geometry, theme, second-instance
    /// arguments, file paths, notification actions). They are delivered only to windows that declared
    /// receive authorization in their capability <c>events</c> list. Application-defined <c>user://</c>
    /// events carry no native data and reach any window.
    /// </summary>
    private bool MayReceive(string label, string eventName)
    {
        if (!EventNames.IsReserved(eventName))
        {
            return true;
        }

        return registry.TryGetCapabilities(label, out var capabilities) &&
               capabilities.AllowsEvent(eventName);
    }
}
