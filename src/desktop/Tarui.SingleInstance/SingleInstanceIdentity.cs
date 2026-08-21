namespace Tarui.SingleInstance;

/// <summary>
/// Opaque naming for one logical application's single-instance port. <see cref="ApplicationId"/>
/// seeds the cross-process lock name; <see cref="ChannelName"/> seeds the per-user communication
/// endpoint (named pipe on Windows, Unix domain socket elsewhere) that a second instance uses to
/// forward its command-line to the running primary.
/// </summary>
public sealed record SingleInstanceIdentity(string ApplicationId, string ChannelName)
{
    /// <summary>Kernel lock name; on Windows a named mutex, on Unix a lock file under the temp directory.</summary>
    public string LockName => $"tarui.net-{ApplicationId}";

    /// <summary>Named pipe (Windows) or socket file (Unix) identifier.</summary>
    public string SocketPath => Path.Combine(Path.GetTempPath(), $"tarui.net-{ChannelName}.sock");
}