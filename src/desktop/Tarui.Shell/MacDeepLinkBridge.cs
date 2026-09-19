using Microsoft.Extensions.Hosting;

namespace Tarui.Shell;

/// <summary>
/// Bridges macOS AppKit <c>application(_:openURLs:)</c> activations into the in-process
/// <see cref="DeepLinkService"/> URL stream. The implementation is split across two partial files:
/// <see cref="MacDeepLinkBridge.Macos"/> on macOS uses <c>NSAppleEventManager</c> +
/// <c>Foundation</c> bindings; <see cref="MacDeepLinkBridge.NoOp"/> on every other platform is an
/// inert hosted service so the composition root stays platform-agnostic. The bridge owns no
/// native resources beyond the registered AppleEvent handler, which is replaced (not appended)
/// on every <see cref="StartAsync"/> call — AppleEvent manager semantics guarantee last writer wins.
/// </summary>
public abstract partial class MacDeepLinkBridge : IHostedService
{
    /// <inheritdoc />
    public abstract Task StartAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public abstract Task StopAsync(CancellationToken cancellationToken);
    /// <summary>
    /// Performs platform detection. The composition root uses the returned factory to decide
    /// which partial implementation registers; callers never need a separate <c>#if MACOS</c>
    /// branch at the call site.
    /// </summary>
    public static Func<DeepLinkService, IMacDeepLinkUrlExtractor, MacDeepLinkBridge> Factory =>
        OperatingSystem.IsMacOS()
            ? (service, extractor) => new Macos(service, extractor)
            : (_, _) => new NoOp();
}