using Microsoft.Extensions.Hosting;
using Tarui.Ipc;

namespace Tarui.Hosting;

/// <summary>
/// Bridges the plugin-facing <see cref="IAppShutdown"/> to the host lifecycle. Shutdown is requested
/// through <see cref="IHostApplicationLifetime.StopApplication"/> so every <c>IHostedService</c> and
/// desktop resource is released before the process exits. The requested exit code is recorded on
/// <see cref="Environment.ExitCode"/> so a graceful stop can honor the caller's intent.
///
/// Relaunch performs a parent-child handshake so the parent does not release the single-instance
/// lock and exit before the child has acquired it. The child signals the named event as soon as
/// <see cref="Tarui.SingleInstance.SingleInstanceGuard.Acquire"/> returns a Primary role; the
/// parent waits on the same event with a bounded timeout before shutting itself down.
/// </summary>
public sealed class HostAppShutdown(IHostApplicationLifetime lifetime) : IAppShutdown
{
    /// <summary>
    /// The maximum time the parent process is willing to wait for the child to confirm it has
    /// acquired the single-instance lock. After this elapses the parent stops waiting and proceeds
    /// with its normal shutdown, which avoids wedging a relaunch on a flaky child start.
    /// </summary>
    public static TimeSpan RelaunchHandshakeTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public void RequestShutdown(int exitCode = 0)
    {
        Environment.ExitCode = exitCode;
        lifetime.StopApplication();
    }

    public bool TryStartRelaunch()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return false;
        }

        var handshakeName = BuildRelaunchHandshakeName();
        using EventWaitHandle? parentWait = AcquireRelaunchEvent(createNew: true, name: handshakeName);
        if (parentWait is null)
        {
            // Could not create the handshake event (permissions, etc.); fall back to the old
            // fire-and-forget behavior so a relaunch is still possible.
            return FireAndForgetRelaunch(executable);
        }

        parentWait.Reset();

        var startInfo = new System.Diagnostics.ProcessStartInfo(executable)
        {
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(RelaunchHandshakeArgument);
        startInfo.ArgumentList.Add(handshakeName);

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        // Wait for the child to acquire the single-instance lock and signal us, with a bounded
        // timeout so a crashed or wedged child never blocks the parent's shutdown.
        var signaled = parentWait.WaitOne(RelaunchHandshakeTimeout);
        return signaled;
    }

    private static bool FireAndForgetRelaunch(string executable)
    {
        using var process = System.Diagnostics.Process.Start(executable);
        return process is not null;
    }

    /// <summary>
    /// Builds a deterministic event name shared by the parent and the child. The argument is
    /// exposed for tests; production callers should not supply it themselves.
    /// </summary>
    public static string BuildRelaunchHandshakeName() =>
        $"tarui-relaunch-{Environment.ProcessId}-{Guid.NewGuid():N}";

    /// <summary>
    /// Command-line flag the child expects to receive from the parent so it knows to sign off on
    /// the handshake event instead of treating the relaunch as a fresh launch.
    /// </summary>
    public const string RelaunchHandshakeArgument = "--tarui-relaunch-handshake";

    /// <summary>
    /// If the supplied arguments indicate the process was launched as part of a relaunch, return
    /// the handshake event name so a caller's startup code can open and signal it. Returns
    /// <see langword="null"/> for non-relaunch launches.
    /// </summary>
    public static string? TryReadRelaunchHandshakeName(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i + 1 < arguments.Count; i++)
        {
            if (string.Equals(arguments[i], RelaunchHandshakeArgument, StringComparison.Ordinal))
            {
                return arguments[i + 1];
            }
        }

        return null;
    }

    private static EventWaitHandle? AcquireRelaunchEvent(bool createNew, string name)
    {
        try
        {
            var eventHandle = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, name, out var createdNew);
            if (!createNew && !createdNew)
            {
                eventHandle.Dispose();
                return null;
            }

            return eventHandle;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (System.IO.IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens (without creating) the handshake event a parent is waiting on. Returns
    /// <see langword="null"/> when the named event cannot be opened, which lets the caller fall
    /// back to a normal launch.
    /// </summary>
    public static EventWaitHandle? OpenRelaunchHandshake(string name) =>
        AcquireRelaunchEvent(createNew: false, name: name);
}
