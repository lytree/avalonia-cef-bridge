using Microsoft.Extensions.Hosting;
using Tarui.Ipc;

namespace Tarui.Hosting;

/// <summary>
/// Bridges the plugin-facing <see cref="IAppShutdown"/> to the host lifecycle. Shutdown is requested
/// through <see cref="IHostApplicationLifetime.StopApplication"/> so every <c>IHostedService</c> and
/// desktop resource is released before the process exits. The requested exit code is recorded on
/// <see cref="Environment.ExitCode"/> so a graceful stop can honor the caller's intent.
/// </summary>
public sealed class HostAppShutdown(IHostApplicationLifetime lifetime) : IAppShutdown
{
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

        using var process = System.Diagnostics.Process.Start(executable);
        return process is not null;
    }
}