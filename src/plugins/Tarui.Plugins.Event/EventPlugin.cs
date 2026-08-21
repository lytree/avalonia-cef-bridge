using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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

public sealed class EventPlugin(IEventSender sender) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            "core:event|emit",
            TaruiJsonContext.Default.EventEmitOptions,
            TaruiJsonContext.Default.Unit,
            async (options, _, cancellationToken) =>
            {
                // Web-originated events are confined to the user:// namespace so the renderer
                // can never forge native events (app://, window://, shell://, ...).
                EventNames.ValidateWebEmit(options.Event);
                await sender.EmitAsync(options.Event, options.Payload, options.TargetWindow, cancellationToken);
                return new Unit();
            },
            "core:event|emit");
    }
}

public static class EventPluginServiceCollectionExtensions
{
    public static IServiceCollection AddEventPlugin(this IServiceCollection services)
        => services.AddPlugin<EventPlugin>();
}
