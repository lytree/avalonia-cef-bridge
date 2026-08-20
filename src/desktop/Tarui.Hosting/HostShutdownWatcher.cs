using Microsoft.Extensions.Hosting;

namespace Tarui.Hosting;

public sealed class HostShutdownWatcher(IHostApplicationLifetime lifetime, TaruiLifetimeBridge bridge) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStopping.Register(bridge.RequestShutdown);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
