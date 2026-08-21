using Tarui.Contracts;

namespace Tarui.Plugins.DeepLink;

/// <summary>
/// Resolves the current launch deep-link URL. The desktop implementation owns the URL stream:
/// it seeds from the primary process's startup arguments (cold activation), observes forwarded
/// second-instance arguments (warm activation on Windows/Linux), and bridges macOS AppKit
/// <c>openURLs</c> activations. Web code reads the result through <c>get-current</c>.
/// </summary>
public interface IDeepLinkService
{
    /// <summary>
    /// Returns the URL that started the running instance, or <see langword="null"/> when the
    /// instance was not activated through a registered custom protocol.
    /// </summary>
    ValueTask<DeepLinkCurrentResult> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Feeds a URL through the exact deep-link validation path so the example app can demonstrate
    /// the received / rejected / not-applicable states without a real protocol activation.
    /// </summary>
    ValueTask<Unit> FeedAsync(DeepLinkFeedOptions options, CancellationToken cancellationToken);
}