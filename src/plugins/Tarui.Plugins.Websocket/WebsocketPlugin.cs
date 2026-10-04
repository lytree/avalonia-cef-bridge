using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Http;

namespace Tarui.Plugins.Websocket;

/// <summary>Executes capability-scoped websocket connections on behalf of the web layer.</summary>
public interface IWebsocketService
{
    /// <summary>
    /// Performs the handshake and starts a fire-and-forget receive loop that pushes every
    /// <see cref="WsMessageFrame"/> (text, binary, and the terminal closed frame) to the channel named by
    /// <see cref="WsConnectOptions.OnMessage"/>. The connection is removed from the registry when it closes.
    /// </summary>
    ValueTask<WsConnectResult> ConnectAsync(
        WsConnectOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken);

    /// <summary>Sends a text or binary payload over the connection with the given id.</summary>
    ValueTask<Unit> SendAsync(WsSendOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Performs a close handshake for the connection with the given id. Closing an unknown or already-closed
    /// id is a no-op so a front-end close race cannot fail the command.
    /// </summary>
    ValueTask<Unit> CloseAsync(WsCloseOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Default websocket service. Keeps one <see cref="ClientWebSocket"/> per connection id; every receive loop
/// outlives its originating command and removes the entry when the socket closes for any reason.
/// </summary>
public sealed class WebsocketService : IWebsocketService
{
    private readonly ConcurrentDictionary<string, ClientWebSocket> _clients = new(StringComparer.Ordinal);
    private int _sequence;

    /// <summary>Default close code surfaced to the front-end when the transport fails without a close frame.</summary>
    private const int AbnormalClosureCode = 1006;

    public async ValueTask<WsConnectResult> ConnectAsync(
        WsConnectOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeWs && uri.Scheme != Uri.UriSchemeWss))
        {
            throw new InvalidOperationException("Only ws:// and wss:// URLs are supported.");
        }

        if (!UrlScopeMatcher.AllowsUrl(allow, deny, options.Url))
        {
            throw new ScopeDeniedException(WebsocketPlugin.ConnectCommand);
        }

        var client = new ClientWebSocket();
        foreach (var protocol in options.Protocols ?? [])
        {
            client.Options.AddSubProtocol(protocol);
        }

        foreach (var header in options.Headers ?? [])
        {
            client.Options.SetRequestHeader(header.Name, header.Value);
        }

        try
        {
            await client.ConnectAsync(uri, cancellationToken);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        var id = $"ws-{Interlocked.Increment(ref _sequence)}";
        _clients[id] = client;
        var channel = ChannelContext.Bind<WsMessageFrame>(options.OnMessage);
        FireAndForget.Run(ReceiveLoopAsync(id, client, channel));
        return new WsConnectResult(id);
    }

    public async ValueTask<Unit> SendAsync(WsSendOptions options, CancellationToken cancellationToken)
    {
        var client = Resolve(options.Id);
        if (options.Text is { } text)
        {
            await client.SendAsync(
                Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        else if (options.BinaryBase64 is { } base64)
        {
            await client.SendAsync(
                Convert.FromBase64String(base64), WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("The send payload must carry either text or binary content.");
        }

        return new Unit();
    }

    public async ValueTask<Unit> CloseAsync(WsCloseOptions options, CancellationToken cancellationToken)
    {
        if (!_clients.TryGetValue(options.Id, out var client))
        {
            return new Unit();
        }

        var code = (WebSocketCloseStatus)(options.Code ?? (int)WebSocketCloseStatus.NormalClosure);
        if (client.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await client.CloseOutputAsync(code, options.Reason, cancellationToken);
        }

        return new Unit();
    }

    private ClientWebSocket Resolve(string id) =>
        _clients.TryGetValue(id, out var client)
            ? client
            : throw new InvalidOperationException($"No websocket connection is open with id '{id}'.");

    /// <summary>
    /// Reads frames until the socket closes (server close frame, local close handshake, or transport failure),
    /// pushing one frame per message, then removes the connection from the registry. Exactly one terminal
    /// closed frame is pushed on every exit path.
    /// </summary>
    private async Task ReceiveLoopAsync(string id, ClientWebSocket client, TaruiChannel<WsMessageFrame> channel)
    {
        var code = AbnormalClosureCode;
        var reason = "abnormal closure";
        var closed = false;
        try
        {
            var buffer = new byte[64 * 1024];
            while (!closed)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        code = (int)client.CloseStatus.GetValueOrDefault(WebSocketCloseStatus.NormalClosure);
                        reason = client.CloseStatusDescription ?? string.Empty;
                        closed = true;
                        break;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (closed)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    await channel.SendAsync(new WsMessageFrame("text", Text: Encoding.UTF8.GetString(message.ToArray())));
                }
                else
                {
                    await channel.SendAsync(new WsMessageFrame("binary", DataBase64: Convert.ToBase64String(message.ToArray())));
                }
            }
        }
        catch (Exception)
        {
            // Transport failure or a torn-down channel sink: keep the 1006 defaults.
        }
        finally
        {
            _clients.TryRemove(id, out _);
            client.Dispose();
        }

        await channel.SendAsync(new WsMessageFrame("closed", Code: code, Reason: reason));
    }
}

/// <summary>Authorizes <see cref="WsConnectOptions"/> URL scopes for the connect command.</summary>
public static class WsScopeAuthorizer
{
    public static bool AllowsUrl(WsConnectOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        UrlScopeMatcher.AllowsUrl(allow, deny, options.Url);
}

/// <summary>
/// Registers the <c>plugin:websocket|connect|send|close</c> commands. Connect is gated by URL scopes with
/// default deny: a bare permission without an allow scope may not open any host (see
/// <see cref="UrlScopeMatcher"/> for the glob semantics); send and close operate on already-authorized
/// connections and carry no scope of their own.
/// </summary>
public sealed class WebsocketPlugin(IWebsocketService service) : ITaruiPlugin
{
    public const string ConnectCommand = "plugin:websocket|connect";
    public const string SendCommand = "plugin:websocket|send";
    public const string CloseCommand = "plugin:websocket|close";

    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            ConnectCommand,
            TaruiJsonContext.Default.WsConnectOptions,
            TaruiJsonContext.Default.WsConnectResult,
            (options, context, ct) =>
            {
                // Websocket 默认拒绝：未带 URL 作用域的裸权限不得静默放开全部主机。
                if (!context.Capabilities.TryGetScope(ConnectCommand, out var scope))
                {
                    throw new ScopeDeniedException(ConnectCommand);
                }

                return service.ConnectAsync(options, scope.Allow, scope.Deny, ct);
            },
            ConnectCommand,
            WsScopeAuthorizer.AllowsUrl);

        commands.Add(
            SendCommand,
            TaruiJsonContext.Default.WsSendOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) => service.SendAsync(options, ct),
            SendCommand);

        commands.Add(
            CloseCommand,
            TaruiJsonContext.Default.WsCloseOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) => service.CloseAsync(options, ct),
            CloseCommand);
    }
}

public static class WebsocketPluginServiceCollectionExtensions
{
    public static IServiceCollection AddWebsocketPlugin(this IServiceCollection services) => services
        .AddSingleton<IWebsocketService, WebsocketService>()
        .AddPlugin<WebsocketPlugin>();
}
