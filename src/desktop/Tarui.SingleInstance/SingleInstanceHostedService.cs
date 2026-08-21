using Microsoft.Extensions.Hosting;

namespace Tarui.SingleInstance;

/// <summary>
/// Starts the primary instance's channel listener when the host starts and stops it when the host
/// stops, so the instance port is released in lockstep with the rest of the desktop resources.
/// </summary>
public sealed class SingleInstanceHostedService(SingleInstanceCoordinator coordinator)
    : IHostedService, IDisposable
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        coordinator.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => coordinator.Dispose();
}