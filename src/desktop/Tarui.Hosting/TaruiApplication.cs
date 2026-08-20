using Avalonia;
using Microsoft.Extensions.Hosting;

namespace Tarui.Hosting;

public sealed class TaruiApplication(IHost host, string[] args) : IDisposable
{
    public IServiceProvider Services => host.Services;

    public Task StartAsync(CancellationToken cancellationToken = default) => host.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) => host.StopAsync(cancellationToken);

    public void Dispose() => host.Dispose();

    public void Run() => RunAsync().GetAwaiter().GetResult();

    private async Task RunAsync()
    {
        try
        {
            await host.StartAsync();
        }
        catch
        {
            host.Dispose();
            throw;
        }

        try
        {
            AppBuilder.Configure(() => new TaruiAvaloniaApp(host.Services))
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try
            {
                await host.StopAsync();
            }
            finally
            {
                host.Dispose();
            }
        }
    }
}
