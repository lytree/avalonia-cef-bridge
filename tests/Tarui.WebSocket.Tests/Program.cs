using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Websocket;

namespace Tarui.WebSocket.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        PluginRegistersThreeCommands();
        await DeniesConnectOutsideScopeAsync();
        await DeniesConnectWithoutScopeAsync();
        await FailsSendForUnknownIdAsync();
        await EchoesTextAndClosedFramesAsync();
        Console.WriteLine("Tarui.WebSocket self-tests passed.");
        return 0;
    }

    private static void PluginRegistersThreeCommands()
    {
        var builder = new CommandRouterBuilder();
        new WebsocketPlugin(new WebsocketService()).ConfigureCommands(builder);
        var router = builder.Build();

        Assert(router.Commands.Count == 3, $"The websocket plugin must register 3 commands, got {router.Commands.Count}.");
        Assert(router.Commands.Contains(WebsocketPlugin.ConnectCommand), "The connect command must be registered.");
        Assert(router.Commands.Contains(WebsocketPlugin.SendCommand), "The send command must be registered.");
        Assert(router.Commands.Contains(WebsocketPlugin.CloseCommand), "The close command must be registered.");
        Assert(router.RegisteredPermissions.Count == 3, "Each websocket command must register exactly one permission.");
    }

    private static async Task DeniesConnectOutsideScopeAsync()
    {
        var dispatcher = NewDispatcher();

        // 允许作用域指向端口 1，目标端口 9 的请求必被拒（且绝不发起连接）。
        var response = await DispatchConnectAsync(
            dispatcher, "ws://127.0.0.1:9/echo", WebsocketCapability(["ws://127.0.0.1:1/**"]));

        Assert(!response!.Success, "A URL outside the allow scope must be denied.");
        Assert(response.Error?.Code == "SCOPE_DENIED", "An out-of-scope URL must surface as SCOPE_DENIED.");
    }

    private static async Task DeniesConnectWithoutScopeAsync()
    {
        var dispatcher = NewDispatcher();

        var response = await DispatchConnectAsync(
            dispatcher, "ws://127.0.0.1:9/echo", WebsocketCapability());

        Assert(!response!.Success, "A bare permission with no URL scope must default to denied.");
        Assert(response.Error?.Code == "SCOPE_DENIED", "The default-deny must surface as SCOPE_DENIED.");
    }

    private static async Task FailsSendForUnknownIdAsync()
    {
        var dispatcher = NewDispatcher();
        var caps = WebsocketCapability(allow: null, extra: [WebsocketPlugin.SendCommand], scoped: false);

        var response = await Dispatch(dispatcher, WebsocketPlugin.SendCommand,
            new WsSendOptions("ws-does-not-exist", Text: "ping"),
            TaruiJsonContext.Default.WsSendOptions, caps);

        Assert(!response!.Success, "Sending over an unknown connection id must fail.");
        Assert(response.Error?.Code == "COMMAND_FAILED", "An unknown id must surface as COMMAND_FAILED.");
    }

    private static async Task EchoesTextAndClosedFramesAsync()
    {
        using var server = EchoServer.TryStart();
        if (server is null)
        {
            Console.WriteLine("Skipped: EchoesTextAndClosedFramesAsync requires a WebSocket-capable HttpListener on this platform.");
            return;
        }

        var dispatcher = NewDispatcher();
        var sink = new RecordingChannelSink();
        var caps = WebsocketCapability(
            allow: [$"ws://127.0.0.1:{server.Port}/**"],
            extra: [WebsocketPlugin.SendCommand, WebsocketPlugin.CloseCommand]);

        var connect = await Dispatch(dispatcher, WebsocketPlugin.ConnectCommand,
            new WsConnectOptions($"{server.Url}?client=tarui", OnMessage: "chan-ws"),
            TaruiJsonContext.Default.WsConnectOptions, caps, sink);
        Assert(connect!.Success, "A scoped connect against the echo server must succeed.");
        var id = connect.Payload!.Value.Deserialize(TaruiJsonContext.Default.WsConnectResult)!.Id;

        var send = await Dispatch(dispatcher, WebsocketPlugin.SendCommand,
            new WsSendOptions(id, Text: "hello-tarui"),
            TaruiJsonContext.Default.WsSendOptions, caps);
        Assert(send!.Success, "Sending text over the echo connection must succeed.");

        var text = await WaitForFrameAsync(sink, frame => frame.Kind == "text", TimeSpan.FromSeconds(10));
        Assert(text.Text == "hello-tarui", "The echoed text frame must round-trip the payload.");

        var close = await Dispatch(dispatcher, WebsocketPlugin.CloseCommand,
            new WsCloseOptions(id, Code: 1000, Reason: "tarui-test"),
            TaruiJsonContext.Default.WsCloseOptions, caps);
        Assert(close!.Success, "Closing the echo connection must succeed.");

        var closed = await WaitForFrameAsync(sink, frame => frame.Kind == "closed", TimeSpan.FromSeconds(10));
        Assert(closed.Code == 1000, $"The closed frame must carry the close code, got {closed.Code}.");
        Assert(sink.Frames.Count(frame => frame.Deserialize(TaruiJsonContext.Default.WsMessageFrame)!.Kind == "closed") == 1,
            "Exactly one closed frame must be pushed for the connection.");

        var resend = await Dispatch(dispatcher, WebsocketPlugin.SendCommand,
            new WsSendOptions(id, Text: "again"),
            TaruiJsonContext.Default.WsSendOptions, caps);
        Assert(!resend!.Success, "Sending after close must fail with COMMAND_FAILED.");
        Assert(resend.Error?.Code == "COMMAND_FAILED", "A closed connection id must surface as COMMAND_FAILED.");
    }

    // ---------- helpers ----------

    private static IpcDispatcher NewDispatcher()
    {
        var builder = new CommandRouterBuilder();
        new WebsocketPlugin(new WebsocketService()).ConfigureCommands(builder);
        return new IpcDispatcher(builder.Build());
    }

    private static async Task<InvokeResponse?> Dispatch<TArgs>(
        IpcDispatcher dispatcher,
        string command,
        TArgs arguments,
        JsonTypeInfo<TArgs> argsType,
        CapabilitySet caps,
        RecordingChannelSink? sink = null)
        where TArgs : notnull
    {
        var request = new InvokeRequest(1, "ws-" + DateTime.UtcNow.Ticks, command,
            JsonSerializer.SerializeToElement(arguments, argsType));
        var json = JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest);
        var responseText = await dispatcher.DispatchJsonAsync(json, new CommandContext("main", "main", caps), sink);
        return JsonSerializer.Deserialize(responseText, TaruiJsonContext.Default.InvokeResponse);
    }

    private static Task<InvokeResponse?> DispatchConnectAsync(
        IpcDispatcher dispatcher, string url, CapabilitySet caps) =>
        Dispatch(dispatcher, WebsocketPlugin.ConnectCommand, new WsConnectOptions(url),
            TaruiJsonContext.Default.WsConnectOptions, caps);

    /// <summary>Builds a capability set for the websocket commands; connect scopes are optional.</summary>
    private static CapabilitySet WebsocketCapability(
        string[]? allow = null,
        string[]? extra = null,
        bool scoped = true)
    {
        var permissions = extra is null
            ? new List<string> { WebsocketPlugin.ConnectCommand }
            : [WebsocketPlugin.ConnectCommand, .. extra];

        var scopedPermissions = new List<KeyValuePair<string, PermissionScope>>();
        if (scoped)
        {
            scopedPermissions.Add(new KeyValuePair<string, PermissionScope>(
                WebsocketPlugin.ConnectCommand,
                new PermissionScope(
                    [.. (allow ?? []).Select(pattern => new PathScope(Path: pattern))],
                    [])));
        }

        return new CapabilitySet(permissions, events: [], scopedPermissions: scopedPermissions);
    }

    private static async Task<WsMessageFrame> WaitForFrameAsync(
        RecordingChannelSink sink,
        Func<WsMessageFrame, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = sink.Frames
                .Select(element => element.Deserialize(TaruiJsonContext.Default.WsMessageFrame)!)
                .FirstOrDefault(predicate);
            if (frame is not null)
            {
                return frame;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException("The expected websocket frame did not arrive in time.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingChannelSink : IChannelSink
    {
        public List<JsonElement> Frames { get; } = [];

        public ValueTask SendAsync(string channelId, JsonElement payload, CancellationToken cancellationToken = default)
        {
            Frames.Add(payload);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A loopback websocket echo server on a random port. Returns <c>null</c> when the platform's
    /// HttpListener cannot serve websocket upgrades so the handshake test can skip honestly.
    /// </summary>
    private sealed class EchoServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();

        public int Port { get; }

        public string Url => $"ws://127.0.0.1:{Port}/echo";

        public static EchoServer? TryStart()
        {
            try
            {
                return new EchoServer();
            }
            catch (Exception exception) when (
                exception is HttpListenerException or PlatformNotSupportedException or NotSupportedException)
            {
                return null;
            }
        }

        private EchoServer()
        {
            Port = GetFreePort();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/echo/");
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        private static int GetFreePort()
        {
            var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            socket.Start();
            try
            {
                return ((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
            }
            finally
            {
                socket.Stop();
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                var accept = await context.AcceptWebSocketAsync(null);
                _ = Task.Run(() => EchoAsync(accept.WebSocket));
            }
        }

        private static async Task EchoAsync(System.Net.WebSockets.WebSocket socket)
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        return;
                    }

                    await socket.SendAsync(
                        new ArraySegment<byte>(buffer, 0, result.Count),
                        result.MessageType,
                        result.EndOfMessage,
                        CancellationToken.None);
                }
            }
            catch (Exception)
            {
                // Client-side close races are expected; the echo loop simply ends.
            }
            finally
            {
                socket.Dispose();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _listener.Stop();
            }
            catch (Exception)
            {
                // The listener may already be gone; disposal is best-effort.
            }

            _cts.Dispose();
        }
    }
}
