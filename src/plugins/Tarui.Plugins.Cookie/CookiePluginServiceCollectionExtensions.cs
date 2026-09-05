using Microsoft.Extensions.DependencyInjection;
using Tarui.Ipc;
using Tarui.WebView.Abstractions;

namespace Tarui.Plugins.Cookie;

/// <summary>
/// Registers the cookie service and <see cref="CookiePlugin"/>. The service resolves the host's
/// <see cref="IWebViewCookieManager"/> when one is registered (for example via
/// <c>AddCefGlueWebView</c>) and otherwise falls back to <see cref="NoopCookieManager"/>, so the plugin is safe
/// to add before or after the browser layer is wired up.
/// </summary>
public static class CookiePluginServiceCollectionExtensions
{
    public static IServiceCollection AddCookiePlugin(this IServiceCollection services) => services
        .AddSingleton<ICookieService>(provider => new CookieService(
            provider.GetService<IWebViewCookieManager>() ?? NoopCookieManager.Instance))
        .AddPlugin<CookiePlugin>();
}