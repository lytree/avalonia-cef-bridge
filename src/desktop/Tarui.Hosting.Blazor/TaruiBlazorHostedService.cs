using Microsoft.Extensions.Hosting;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// Starts the embedded Blazor server when the Tarui host starts and stops it during host shutdown.
/// The Blazor URL is published through <see cref="TaruiBlazorServer.StartUri"/> so the window URL
/// applier can pick it up before the window opens.
/// </summary>
internal sealed class TaruiBlazorHostedService(TaruiBlazorServer server) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await server.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await server.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// After host start, copies the resolved Blazor URL onto the main window builder when the user has
/// not already supplied a window URL of their own. This keeps <c>AddTaruiBlazor</c> from clobbering
/// explicit host configuration while still offering a "just works" experience.
/// </summary>
internal sealed class TaruiBlazorWindowUrlApplier(
    TaruiBlazorServer server,
    TaruiWindowBuilder windowBuilder) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The hosted services run in registration order: TaruiBlazorHostedService first, then this
        // applier. If a future user adds more appliers after us, those run after this and may
        // override the URL — that is the expected behaviour.
        if (windowBuilder.Url is null)
        {
            windowBuilder.Url = server.StartUri.ToString();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}