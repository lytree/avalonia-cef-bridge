namespace Tarui.Contracts;

/// <summary>A single named header value applied to a websocket handshake.</summary>
public sealed record WsHeader(string Name, string Value);

/// <summary>
/// Request for the <c>plugin:websocket|connect</c> command. <see cref="Url"/> is validated against the
/// caller capability's allow/deny URL scopes (default deny) before the handshake starts. <see cref="OnMessage"/>
/// is the channel token the receive loop pushes <see cref="WsMessageFrame"/>s to.
/// </summary>
public sealed record WsConnectOptions(
    string Url,
    string[]? Protocols = null,
    WsHeader[]? Headers = null,
    string? OnMessage = null);

/// <summary>Result of a successful websocket handshake: the id used by the send/close commands.</summary>
public sealed record WsConnectResult(string Id);

/// <summary>
/// A single frame streamed to the <see cref="WsConnectOptions.OnMessage"/> channel. <c>Kind</c> is
/// <c>"text"</c> carrying <see cref="Text"/>, <c>"binary"</c> carrying <see cref="DataBase64"/>, or the
/// terminal <c>"closed"</c> frame carrying <see cref="Code"/>/<see cref="Reason"/>.
/// </summary>
public sealed record WsMessageFrame(
    string Kind,
    string? Text = null,
    string? DataBase64 = null,
    int? Code = null,
    string? Reason = null);

/// <summary>
/// Request for <c>plugin:websocket|send</c>. Exactly one of <see cref="Text"/> or <see cref="BinaryBase64"/>
/// must be provided; the payload type selects the websocket message type.
/// </summary>
public sealed record WsSendOptions(string Id, string? Text = null, string? BinaryBase64 = null);

/// <summary>
/// Request for <c>plugin:websocket|close</c>. <see cref="Code"/> defaults to a normal closure (1000) when
/// omitted; closing an unknown or already-closed id is a no-op.
/// </summary>
public sealed record WsCloseOptions(string Id, int? Code = null, string? Reason = null);
