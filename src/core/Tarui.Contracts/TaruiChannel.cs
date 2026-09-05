using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace Tarui.Contracts;

/// <summary>
/// Receives streamed channel payloads and routes them to a target web view. A single implementation is
/// scoped per transport session (see <c>WebviewSession</c>) so every invocation is pushed back to the exact
/// surface that created the channel.
/// </summary>
public interface IChannelSink
{
    /// <summary>Delivers one streamed payload to the channel identified by <paramref name="channelId"/>.</summary>
    ValueTask SendAsync(string channelId, JsonElement payload, CancellationToken cancellationToken = default);
}

/// <summary>Holds the sink current dispatch so <see cref="ChannelContext.Bind{T}"/> can bind an incoming channel token.</summary>
public sealed class ChannelSinkContext
{
    private static readonly AsyncLocal<IChannelSink?> _current = new();

    /// <summary>The sink to bind incoming channels against for the current dispatch, if any.</summary>
    public static IChannelSink? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

/// <summary>
/// Resolves a channel token (a plain string id carried in an invocation payload) into a type-safe
/// <see cref="TaruiChannel{T}"/> bound to the current dispatch's <see cref="IChannelSink"/>. Binding happens
/// at a compile-time generic call site so no runtime reflection is used.
/// </summary>
public static class ChannelContext
{
    /// <summary>
    /// Returns a <see cref="TaruiChannel{T}"/> bound to the invoking surface. When no sink is in scope or the
    /// token is empty, an unbound in-memory channel is returned so library/test paths keep working.
    /// </summary>
    public static TaruiChannel<T> Bind<T>(string? channelId)
    {
        var sink = ChannelSinkContext.Current;
        if (sink is null || string.IsNullOrEmpty(channelId))
        {
            return new TaruiChannel<T>();
        }

        var payloadType = (JsonTypeInfo<T>)TaruiJsonContext.Default.GetTypeInfo(typeof(T))!;
        return TaruiChannel<T>.Bind(sink, channelId, payloadType);
    }
}

/// <summary>
/// A streaming argument that a native command receives and writes incrementally. When bound to the invoking
/// surface's <see cref="IChannelSink"/> (via <see cref="ChannelContext.Bind{T}"/>) <see cref="SendAsync(T, CancellationToken)"/>
/// pushes each payload back to the front-end. Without a sink it degrades to an in-memory buffered channel
/// (readable via <see cref="ReadAllAsync"/>) so it remains testable outside a running host.
/// </summary>
public sealed class TaruiChannel<T>
{
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly IChannelSink? _sink;
    private readonly string? _id;
    private readonly JsonTypeInfo<T>? _jsonTypeInfo;

    /// <summary>The sink-local identifier this channel was bound with, or <c>null</c> when unbound.</summary>
    public string? Id => _id;

    // Bound: SendAsync routes through the web-view sink. Unbound: writes into the in-memory buffer.
    private TaruiChannel(IChannelSink? sink, string? id, JsonTypeInfo<T>? jsonTypeInfo)
    {
        _sink = sink;
        _id = id;
        _jsonTypeInfo = jsonTypeInfo;
    }

    /// <summary>Creates an unbound, in-memory buffered channel (used by tests and library callers).</summary>
    public TaruiChannel()
    {
    }

    /// <summary>Creates a channel bound to a sink so invocations written here are pushed to the front-end.</summary>
    internal static TaruiChannel<T> Bind(IChannelSink sink, string id, JsonTypeInfo<T> jsonTypeInfo)
        => new(sink, id, jsonTypeInfo);

    /// <summary>
    /// Buffers a payload in the in-memory channel when unbound, or pushes it to the front-end when bound.
    /// </summary>
    public ValueTask SendAsync(T value, CancellationToken cancellationToken = default)
    {
        if (_sink is not null && _id is not null && _jsonTypeInfo is not null)
        {
            var element = JsonSerializer.SerializeToElement(value, _jsonTypeInfo);
            return _sink.SendAsync(_id, element, cancellationToken);
        }

        return _channel.Writer.WriteAsync(value, cancellationToken);
    }

    /// <summary>Reads buffered payloads; only meaningful for the unbound in-memory channel.</summary>
    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Completes the in-memory channel.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}