using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Shell;

/// <summary>
/// Routes native events to windows and web views. The two registries stay distinct on purpose: a window
/// event fans out to every web view a window hosts, while a web view event must address one surface by its
/// own label. Resolving both through the window registry only worked while a window hosted exactly one
/// surface whose label happened to match the window's.
/// </summary>
public sealed class EventRouter(
    IWindowSinkRegistry windowRegistry,
    IWebviewSinkRegistry webviewRegistry,
    EventHub hub)
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
        if (MayWindowReceive(label, eventName) &&
            windowRegistry.TryGetSink(label, out var sink))
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
        foreach (var label in windowRegistry.Labels)
        {
            if (MayWindowReceive(label, eventName) &&
                windowRegistry.TryGetSink(label, out var sink))
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
    /// Delivers a <c>webview://</c> reserved event to a specific web view by its own label. The sink and the
    /// capability set are both resolved from the web view registry, so a window hosting several surfaces
    /// addresses each of them independently and authorization is evaluated per surface.
    /// </summary>
    public async ValueTask EmitToWebviewAsync(
        string webviewLabel,
        string eventName,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        hub.Emit(eventName, payload);
        if (MayWebviewReceive(webviewLabel, eventName) &&
            webviewRegistry.TryGetSink(webviewLabel, out var sink))
        {
            await sink.SendEventAsync(eventName, payload, cancellationToken);
        }
    }

    /// <summary>
    /// Reserved native events may carry sensitive system data (window geometry, theme, second-instance
    /// arguments, file paths, notification actions). They are delivered only to surfaces that declared
    /// receive authorization in their capability <c>events</c> list. Application-defined <c>user://</c>
    /// events carry no native data and reach any surface.
    /// </summary>
    private bool MayWindowReceive(string label, string eventName)
    {
        if (!EventNames.IsReserved(eventName))
        {
            return true;
        }

        return windowRegistry.TryGetCapabilities(label, out var capabilities) &&
               capabilities.AllowsEvent(eventName);
    }

    private bool MayWebviewReceive(string webviewLabel, string eventName)
    {
        if (!EventNames.IsReserved(eventName))
        {
            return true;
        }

        return webviewRegistry.TryGetCapabilities(webviewLabel, out var capabilities) &&
               capabilities.AllowsEvent(eventName);
    }
}
