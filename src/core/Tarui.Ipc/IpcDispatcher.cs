using System.Text.Json;
using Tarui.Contracts;

namespace Tarui.Ipc;

public sealed class IpcDispatcher(CommandRouter router)
{
    public async ValueTask<string> DispatchJsonAsync(
        string json,
        CommandContext context,
        IChannelSink? channelSink = null,
        CancellationToken cancellationToken = default)
    {
        var previousSink = ChannelSinkContext.Current;
        ChannelSinkContext.Current = channelSink;
        try
        {
            InvokeResponse? response = null;
            try
            {
                var request = JsonSerializer.Deserialize(json, TaruiJsonContext.Default.InvokeRequest)
                    ?? throw new InvalidPayloadException();
                response = await router.InvokeAsync(request, context, cancellationToken);
            }
            catch (JsonException)
            {
                response = InvokeResponse.Fail("unknown", "INVALID_MESSAGE", "The IPC message is invalid.");
            }
            catch (InvalidPayloadException)
            {
                response = InvokeResponse.Fail("unknown", "INVALID_MESSAGE", "The IPC message is invalid.");
            }

            return JsonSerializer.Serialize(response, TaruiJsonContext.Default.InvokeResponse);
        }
        finally
        {
            ChannelSinkContext.Current = previousSink;
        }
    }
}
