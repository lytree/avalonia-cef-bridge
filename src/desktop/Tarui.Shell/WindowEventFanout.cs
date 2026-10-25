using System.Text.Json;

namespace Tarui.Shell;

/// <summary>
/// The window-level event sink: forwards a <c>window://</c> event to every web view hosted by the
/// window. With a single hosted surface it behaves exactly like delivering to that one session, and
/// it grows naturally once a window hosts several — a surface created or destroyed while an event is
/// in flight is skipped because the label list is a snapshot taken per emission.
/// </summary>
internal sealed class WindowEventFanout(string windowLabel, IWebviewSinkRegistry webviews) : IEventSink
{
    public async ValueTask SendEventAsync(
        string eventName,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        foreach (var label in webviews.WebviewLabelsFor(windowLabel))
        {
            if (!webviews.TryGetSink(label, out var sink))
            {
                continue;
            }

            // Defense in depth: a surface whose own profile does not carry the event is skipped even
            // though its window receives it. In the current model every surface's set is the union of
            // its window profile and webviews-selected grants, so this never narrows a window grant —
            // it keeps the per-webview authorization boundary explicit if surface sets ever diverge.
            if (webviews.TryGetCapabilities(label, out var capabilities) && !capabilities.AllowsEvent(eventName))
            {
                continue;
            }

            await sink.SendEventAsync(eventName, payload, cancellationToken);
        }
    }
}
