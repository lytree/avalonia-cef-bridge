using System.Text.Json;
using Tarui.Contracts;

namespace Tarui.Ipc;

public sealed class IpcDispatcher(CommandRouter router)
{
    public async ValueTask<string> DispatchJsonAsync(
        string json,
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = JsonSerializer.Deserialize(json, TaruiJsonContext.Default.InvokeRequest)
                ?? throw new InvalidPayloadException();
            var response = await router.InvokeAsync(request, context, cancellationToken);
            return JsonSerializer.Serialize(response, TaruiJsonContext.Default.InvokeResponse);
        }
        catch (JsonException)
        {
            var response = InvokeResponse.Fail("unknown", "INVALID_MESSAGE", "The IPC message is invalid.");
            return JsonSerializer.Serialize(response, TaruiJsonContext.Default.InvokeResponse);
        }
        catch (InvalidPayloadException)
        {
            var response = InvokeResponse.Fail("unknown", "INVALID_MESSAGE", "The IPC message is invalid.");
            return JsonSerializer.Serialize(response, TaruiJsonContext.Default.InvokeResponse);
        }
    }
}
