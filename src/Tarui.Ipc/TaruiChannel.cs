using System.Threading.Channels;

namespace Tarui.Ipc;

public sealed class TaruiChannel<T>
{
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask SendAsync(T value, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(value, cancellationToken);

    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete() => _channel.Writer.TryComplete();
}
