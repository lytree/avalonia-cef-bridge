using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tarui.Contracts;

public sealed record InvokeRequest(
    int Protocol,
    string Id,
    string Command,
    JsonElement Payload,
    string WindowLabel = "main",
    string WebViewLabel = "main");

public sealed record InvokeResponse(
    int Protocol,
    string Id,
    bool Success,
    JsonElement? Payload,
    IpcError? Error)
{
    public static InvokeResponse Ok(string id, JsonElement payload) =>
        new(1, id, true, payload, null);

    public static InvokeResponse Fail(string id, string code, string message) =>
        new(1, id, false, null, new IpcError(code, message));
}

public sealed record IpcError(string Code, string Message);

public sealed record ThemeChanged(string Theme);

public sealed record AppHandshake(
    string Product,
    string ShellVersion,
    int BridgeVersion,
    string Platform,
    string[] Capabilities);

public sealed record OpenDialogOptions(
    bool Multiple = false,
    string[]? Extensions = null);

public sealed record OpenDialogResult(string[] Paths);

public sealed record EmptyArgs;

public sealed record Unit;

public sealed record EventEnvelope(
    string Type,
    string Event,
    JsonElement Payload);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(InvokeRequest))]
[JsonSerializable(typeof(InvokeResponse))]
[JsonSerializable(typeof(IpcError))]
[JsonSerializable(typeof(ThemeChanged))]
[JsonSerializable(typeof(AppHandshake))]
[JsonSerializable(typeof(OpenDialogOptions))]
[JsonSerializable(typeof(OpenDialogResult))]
[JsonSerializable(typeof(EmptyArgs))]
[JsonSerializable(typeof(Unit))]
[JsonSerializable(typeof(EventEnvelope))]
[JsonSerializable(typeof(string[]))]
public partial class TaruiJsonContext : JsonSerializerContext;
