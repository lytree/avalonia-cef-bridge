using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Shell;

namespace Tarui.SingleInstance;

/// <summary>
/// Routes second-instance activations to the running primary instance's webviews. Owns the instance
/// channel server (named pipe on Windows, Unix domain socket elsewhere) and a bounded queue for
/// activations that arrive before the main window is registered, so startup-time arguments are not
/// lost. Delivered as <c>app://second-instance</c> events through the shell <see cref="EventRouter"/>,
/// which only forwards reserved native events to windows whose capability grants receive access.
/// </summary>
public sealed class SingleInstanceCoordinator(
    SingleInstanceIdentity identity,
    EventRouter eventRouter,
    IWindowSinkRegistry windows,
    IEnumerable<ISecondActivationSink> activationSinks) : ISingleInstanceCoordinator, IDisposable
{
    private const string EventName = "app://second-instance";
    private const int MaxQueuedActivations = 4;

    private readonly ISecondActivationSink[] _activationSinks = activationSinks.ToArray();
    private readonly ConcurrentQueue<SecondInstanceArgs> _queue = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Thread? _listener;
    private bool _started;
    private bool _disposed;

    /// <summary>Starts the background instance-channel listener. Safe to call once.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
            _cts = new CancellationTokenSource();
        }

        var token = _cts.Token;
        _listener = new Thread(() => RunListener(token)) { IsBackground = true, Name = "single-instance" };
        _listener.Start();
    }

    /// <summary>
    /// Accepts an activation payload from a second instance. When the main window is already
    /// registered it is delivered immediately; otherwise it is queued in the bounded FIFO.
    /// </summary>
    public void Receive(SecondInstanceArgs args)
    {
        NotifySinks(args);

        if (windows.Labels.Contains("main"))
        {
            FireAndForget(eventRouter.EmitToAllAsync(EventName, Serialize(args)));
            return;
        }

        lock (_gate)
        {
            if (_queue.Count >= MaxQueuedActivations && _queue.TryDequeue(out _))
            {
                // Drop the oldest activation to bound memory.
            }

            _queue.Enqueue(args);
        }
    }

    public void Flush()
    {
        while (_queue.TryDequeue(out var args))
        {
            NotifySinks(args);
            FireAndForget(eventRouter.EmitToAllAsync(EventName, Serialize(args)));
        }
    }

    private void NotifySinks(SecondInstanceArgs args)
    {
        foreach (var sink in _activationSinks)
        {
            try
            {
                sink.OnSecondActivation(args);
            }
            catch
            {
                // Sink notification is best-effort and must never break activation delivery.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cts?.Cancel();
        }

        _listener?.Join(TimeSpan.FromSeconds(2));
        _cts?.Dispose();
    }

    private void RunListener(CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            RunNamedPipeListener(token);
        }
        else
        {
            RunUnixSocketListener(token);
        }
    }

    private void RunNamedPipeListener(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(identity.ChannelName, PipeDirection.In, 1);
            try
            {
                server.WaitForConnectionAsync(token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                continue;
            }

            var payload = Deserialize(server);
            if (payload is not null)
            {
                Receive(payload);
            }
        }
    }

    private void RunUnixSocketListener(CancellationToken token)
    {
        var path = identity.SocketPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a stale socket file.
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(1);

        while (!token.IsCancellationRequested)
        {
            try
            {
                using var client = listener.Accept();
                using var network = new NetworkStream(client, ownsSocket: true);
                var payload = Deserialize(network);
                if (payload is not null)
                {
                    Receive(payload);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private static SecondInstanceArgs? Deserialize(Stream stream) =>
        JsonSerializer.Deserialize(stream, TaruiJsonContext.Default.SecondInstanceArgs);

    private static JsonElement Serialize(SecondInstanceArgs args) =>
        JsonSerializer.SerializeToElement(args, TaruiJsonContext.Default.SecondInstanceArgs);

    private static async void FireAndForget(ValueTask task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Second-instance delivery is best-effort; a closed window is not fatal.
        }
    }
}