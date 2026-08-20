using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Events;

public interface IEventSender
{
    ValueTask<Unit> EmitAsync(
        string eventName,
        JsonElement payload,
        string? targetWindow,
        CancellationToken cancellationToken);
}

public static class EventPlugin
{
    public static void Register(
        CommandRouterBuilder commands,
        Action<string> registerPermission,
        IEventSender sender)
    {
        commands.Add(
            "core:event|emit",
            TaruiJsonContext.Default.EventEmitOptions,
            TaruiJsonContext.Default.Unit,
            async (options, _, cancellationToken) =>
            {
                await sender.EmitAsync(options.Event, options.Payload, options.TargetWindow, cancellationToken);
                return new Unit();
            },
            "core:event|emit");
        registerPermission("core:event|emit");
    }
}
