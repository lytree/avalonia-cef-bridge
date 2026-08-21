using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using Tarui.Contracts;

namespace Tarui.SingleInstance;

/// <summary>The role a freshly launched process takes relative to the running system.</summary>
public enum InstanceRole
{
    /// <summary>This process acquired the instance lock and becomes the primary host.</summary>
    Primary,
    /// <summary>A primary already holds the lock; the arguments were forwarded and this process exits.</summary>
    Secondary,
}

/// <summary>
/// Process-lifetime handle returned by <see cref="SingleInstanceGuard.Acquire"/>. Holds the primary
/// lock for the duration of the process and releases it on disposal.
/// </summary>
public sealed class SingleInstanceHandle : IDisposable
{
    private readonly IDisposable? _lock;

    internal SingleInstanceHandle(InstanceRole role, IDisposable? lockKeepAlive)
    {
        Role = role;
        _lock = lockKeepAlive;
    }

    public InstanceRole Role { get; }

    public void Dispose() => _lock?.Dispose();
}

/// <summary>
/// Startup gate for a single-instance desktop application. It must run after the CEF subprocess
/// dispatch but before the host is built, so a secondary process forwards its arguments and exits
/// without ever constructing the host. The primary process holds a cross-process lock and later
/// hosts the instance-channel listener through <see cref="SingleInstanceCoordinator"/>.
/// </summary>
public static class SingleInstanceGuard
{
    private const int ForwardAttempts = 5;
    private const int ForwardTimeoutMillis = 2000;

    public static SingleInstanceHandle Acquire(
        SingleInstanceIdentity identity,
        string[] arguments,
        string? workingDirectory)
    {
        var acquired = AcquireProcessLock(identity);
        if (acquired is not null)
        {
            return new SingleInstanceHandle(InstanceRole.Primary, acquired);
        }

        var payload = new SecondInstanceArgs(
            arguments ?? [],
            workingDirectory ?? string.Empty,
            DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        ForwardToPrimary(identity, payload);
        return new SingleInstanceHandle(InstanceRole.Secondary, null);
    }

    private static IDisposable? AcquireProcessLock(SingleInstanceIdentity identity)
    {
        if (OperatingSystem.IsWindows())
        {
            var mutex = new Mutex(initiallyOwned: false, identity.LockName);
            if (mutex.WaitOne(0))
            {
                return mutex;
            }

            mutex.Dispose();
            return null;
        }

        // On Unix a named Mutex is only process-local, so a lock file enforces exclusivity across
        // processes. The primary keeps the FileShare.None stream open for its lifetime.
        var path = identity.SocketPath + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new FileStreamLock(stream, path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void ForwardToPrimary(SingleInstanceIdentity identity, SecondInstanceArgs payload)
    {
        if (OperatingSystem.IsWindows())
        {
            ForwardOverNamedPipe(identity, payload);
        }
        else
        {
            ForwardOverUnixSocket(identity, payload);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ForwardOverNamedPipe(SingleInstanceIdentity identity, SecondInstanceArgs payload)
    {
        for (var attempt = 0; attempt < ForwardAttempts; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", identity.ChannelName, PipeDirection.Out);
                client.Connect(ForwardTimeoutMillis);
                JsonSerializer.Serialize(client, payload, TaruiJsonContext.Default.SecondInstanceArgs);
                client.WaitForPipeDrain();
                return;
            }
            catch (Exception ex) when (attempt < ForwardAttempts - 1 && IsTransient(ex))
            {
                Thread.Sleep(50);
            }
        }
    }

    private static void ForwardOverUnixSocket(SingleInstanceIdentity identity, SecondInstanceArgs payload)
    {
        var endPoint = new UnixDomainSocketEndPoint(identity.SocketPath);
        for (var attempt = 0; attempt < ForwardAttempts; attempt++)
        {
            try
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(endPoint);
                using var network = new NetworkStream(socket, ownsSocket: true);
                JsonSerializer.Serialize(network, payload, TaruiJsonContext.Default.SecondInstanceArgs);
                network.Flush();
                return;
            }
            catch (Exception ex) when (attempt < ForwardAttempts - 1 && IsTransient(ex))
            {
                Thread.Sleep(50);
            }
        }
    }

    private static bool IsTransient(Exception exception) =>
        exception is IOException or SocketException or TimeoutException;

    private sealed class FileStreamLock(FileStream stream, string path) : IDisposable
    {
        public void Dispose()
        {
            stream.Dispose();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a stale lock file is harmless on next launch.
            }
        }
    }
}