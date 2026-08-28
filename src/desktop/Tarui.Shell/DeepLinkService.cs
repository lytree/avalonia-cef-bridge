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
    private readonly EventRouter _events;
    private readonly IReadOnlySet<string> _schemes;
    private readonly object _gate = new();
    private string? _currentUrl;

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
    /// rejected and never produce an event.
    /// </summary>
    public void Deliver(string url)
    {
        var scheme = DeepLinkUri.TryExtractScheme(url, _schemes);
        if (scheme is null)
        {
            return;
        }

        lock (_gate)
        {
            _currentUrl = url;
        }

        FireAndForget.Run(_events.EmitToAllAsync(
            $"deeplink://{scheme}",
            JsonSerializer.SerializeToElement(url, TaruiJsonContext.Default.String)));
    }

}
