using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Events;

namespace Tarui.Shell;

internal sealed class RoutedEventSender(EventRouter router) : IEventSender
{
    public async ValueTask<Unit> EmitAsync(
        string eventName,
        JsonElement payload,
        string? targetWindow,
        CancellationToken cancellationToken)
    {
        await router.EmitAsync(eventName, payload, targetWindow, cancellationToken);
        return new Unit();
    }
}
