using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Tarui.Shell;

/// <summary>
/// Registers the configured custom protocols with the operating system when the host starts. On
/// Windows this writes per-user <c>HKCU\Software\Classes</c> entries; on Linux it publishes a
/// per-user <c>.desktop</c> <c>x-scheme-handler</c> entry; on macOS registration is a packaging
/// (<c>CFBundleURLTypes</c>) concern rather than a runtime action, but the
/// <see cref="MacDeepLinkBridge"/> is installed so warm <c>application:openURLs:</c> activations
/// reach <see cref="DeepLinkService.Deliver"/>. Registration is idempotent and runs only for the
/// configured scheme set.
/// </summary>
public sealed class DeepLinkRegistrarHostedService(
    IConfiguration configuration,
    DeepLinkService deepLinkService,
    IMacDeepLinkUrlExtractor macUrlExtractor) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var schemes = DeepLinkConfiguration.ReadSchemes(configuration);
        WindowsDeepLinkRegistrar.RegisterSchemes(schemes);
        LinuxDeepLinkRegistrar.RegisterSchemes(schemes);

        // macOS: install the AppleEvent bridge so cold argv + warm openURLs: activations reach
        // the shared URL stream. No-op on every other platform via the partial bridge factory.
        return MacDeepLinkBridge.Factory(deepLinkService, macUrlExtractor).StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}