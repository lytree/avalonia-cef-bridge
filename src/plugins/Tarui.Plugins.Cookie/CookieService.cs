using System.Threading;
using Tarui.Contracts;
using Tarui.WebView.Abstractions;

namespace Tarui.Plugins.Cookie;

/// <summary>
/// Command-facing cookie operations. The implementation is a thin pass-through to the host-supplied
/// <see cref="IWebViewCookieManager"/> so the routing and DTO surface stays uniform across hosts. When no host
/// manager is registered the composition extension injects <see cref="NoopCookieManager"/>, which reports
/// <c>Supported = false</c> rather than inventing an empty cookie store.
/// </summary>
public interface ICookieService
{
    ValueTask<CookieListResult> ListAsync(CookieListOptions options, CancellationToken cancellationToken);

    ValueTask<CookieSetResult> SetAsync(CookieSetOptions options, CancellationToken cancellationToken);

    ValueTask<CookieDeleteResult> RemoveAsync(CookieDeleteOptions options, CancellationToken cancellationToken);

    ValueTask FlushAsync(CancellationToken cancellationToken);
}

public sealed class CookieService(IWebViewCookieManager manager) : ICookieService
{
    public ValueTask<CookieListResult> ListAsync(CookieListOptions options, CancellationToken cancellationToken)
        => manager.ListAsync(options, cancellationToken);

    public ValueTask<CookieSetResult> SetAsync(CookieSetOptions options, CancellationToken cancellationToken)
        => manager.SetAsync(options, cancellationToken);

    public ValueTask<CookieDeleteResult> RemoveAsync(CookieDeleteOptions options, CancellationToken cancellationToken)
        => manager.RemoveAsync(options, cancellationToken);

    public ValueTask FlushAsync(CancellationToken cancellationToken)
        => manager.FlushAsync(cancellationToken);
}

/// <summary>
/// Fallback manager used when no browser host registers an <see cref="IWebViewCookieManager"/>. Every operation
/// reports that the store is unsupported; nothing is fabricated, so callers degrade honestly instead of trusting
/// a fake empty cookie list.
/// </summary>
public sealed class NoopCookieManager : IWebViewCookieManager
{
    private const string UnsupportedMessage = "The cookie store is unavailable on this host (no browser cookie manager is registered).";

    public static NoopCookieManager Instance { get; } = new();

    public ValueTask<CookieListResult> ListAsync(CookieListOptions options, CancellationToken cancellationToken)
        => ValueTask.FromResult(new CookieListResult(false, [], UnsupportedMessage));

    public ValueTask<CookieSetResult> SetAsync(CookieSetOptions options, CancellationToken cancellationToken)
        => ValueTask.FromResult(new CookieSetResult(false, UnsupportedMessage));

    public ValueTask<CookieDeleteResult> RemoveAsync(CookieDeleteOptions options, CancellationToken cancellationToken)
        => ValueTask.FromResult(new CookieDeleteResult(false, UnsupportedMessage));

    public ValueTask FlushAsync(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}