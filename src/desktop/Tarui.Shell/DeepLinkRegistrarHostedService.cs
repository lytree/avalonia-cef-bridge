using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Tarui.Shell;

/// <summary>
/// Registers the configured custom protocols with the operating system when the host starts. On
/// Windows this writes per-user <c>HKCU\Software\Classes</c> entries; other platforms degrade to a
/// no-op (their registration is a packaging/deployment concern). Registration is idempotent and
/// runs only for the configured scheme set.
/// </summary>
public sealed class DeepLinkRegistrarHostedService(IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        WindowsDeepLinkRegistrar.RegisterSchemes(DeepLinkConfiguration.ReadSchemes(configuration));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}