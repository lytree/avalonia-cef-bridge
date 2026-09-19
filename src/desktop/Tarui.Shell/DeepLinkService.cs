using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.DeepLink;

namespace Tarui.Shell;

/// <summary>
/// Owns the application's deep-link URL stream. It seeds the current launch URL from the primary
/// process's startup arguments (cold activation: the OS launches the app with the URL on argv),
/// observes forwarded second-instance arguments (warm activation on Windows/Linux via the single
/// instance channel), and exposes a <see cref="Deliver"/> entry point for the macOS AppKit
/// <c>openURLs</c> delegate bridge. Delivered URLs are reported through the reserved
/// <c>deeplink://&lt;scheme&gt;</c> events, which are gated by per-window capability grants.
/// </summary>
public sealed class DeepLinkService : IDeepLinkService, ISecondActivationSink
{
    /// <summary>
    /// Suppresses event emission when the same URL is delivered twice within this window. macOS
    /// cold activation can route the launch URL through both <c>argv</c> and the AppleEvent handler;
    /// warm activation can also see rapid duplicate <c>openURLs:</c> when an external tool retries.
    /// <see cref="_currentUrl"/> is always updated so <c>get-current</c> reflects the latest value.
    /// </summary>
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(2);

    private readonly EventRouter _events;
    private readonly IReadOnlySet<string> _schemes;
    private readonly object _gate = new();
    private string? _currentUrl;
    private string? _lastDeliveredUrl;
    private DateTimeOffset _lastDeliveredAt = DateTimeOffset.MinValue;

    public DeepLinkService(
        string[] startupArgs,
        IReadOnlyCollection<string> schemes,
        EventRouter events)
    {
        _events = events;
        _schemes = new HashSet<string>(schemes, StringComparer.OrdinalIgnoreCase);

        // Cold activation: seed the current URL from the first registered-scheme URL on argv.
        foreach (var arg in startupArgs)
        {
            if (DeepLinkUri.TryExtractScheme(arg, _schemes) is not null)
            {
                _currentUrl = arg;
                break;
            }
        }
    }

    public ValueTask<DeepLinkCurrentResult> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? current;
        lock (_gate)
        {
            current = _currentUrl;
        }

        return ValueTask.FromResult(new DeepLinkCurrentResult(current));
    }

    public ValueTask<Unit> FeedAsync(DeepLinkFeedOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (options.Url is not null)
        {
            Deliver(options.Url);
        }

        return ValueTask.FromResult(new Unit());
    }

    public void OnSecondActivation(SecondInstanceArgs args)
    {
        foreach (var arg in args.Arguments)
        {
            if (DeepLinkUri.TryExtractScheme(arg, _schemes) is not null)
            {
                Deliver(arg);
                break;
            }
        }
    }

    /// <summary>
    /// Accepts a deep-link URL from any native activation source (warm single-instance URL, macOS
    /// delegate bridge). Invalid URLs (unregistered scheme, control characters, oversized) are
    /// rejected and never produce an event. Repeats of the same URL within <see cref="DedupWindow"/>
    /// update <c>_currentUrl</c> but suppress the <c>deeplink://&lt;scheme&gt;</c> event emission so the
    /// web layer is not asked to handle the same payload twice (macOS cold argv + AppleEvent).
    /// </summary>
    public void Deliver(string url)
    {
        var scheme = DeepLinkUri.TryExtractScheme(url, _schemes);
        if (scheme is null)
        {
            return;
        }

        var emit = false;
        lock (_gate)
        {
            _currentUrl = url;
            var now = DateTimeOffset.UtcNow;
            if (_lastDeliveredUrl != url || now - _lastDeliveredAt >= DedupWindow)
            {
                _lastDeliveredUrl = url;
                _lastDeliveredAt = now;
                emit = true;
            }
        }

        if (!emit)
        {
            return;
        }

        FireAndForget.Run(_events.EmitToAllAsync(
            $"deeplink://{scheme}",
            JsonSerializer.SerializeToElement(url, TaruiJsonContext.Default.String)));
    }

}
