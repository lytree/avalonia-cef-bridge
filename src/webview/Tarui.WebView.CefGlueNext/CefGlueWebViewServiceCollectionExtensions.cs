using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tarui.WebView.Abstractions;
using Tarui.WebView.Avalonia;

namespace Tarui.WebView.CefGlueNext;

public static class CefGlueWebViewServiceCollectionExtensions
{
    // Registering the web view backend also registers the runtime it is built on. The runtime owns
    // the application origin, so TaruiAppOrigin is sourced from it instead of being derived twice.
    public static IServiceCollection AddCefGlueWebView(this IServiceCollection services) => services
        .AddSingleton(sp => CefGlueNextWebAppOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()))
        .AddSingleton(sp => new CefGlueNextWebRuntime(sp.GetRequiredService<CefGlueNextWebAppOptions>()))
        .AddSingleton<ITaruiWebRuntime>(sp => sp.GetRequiredService<CefGlueNextWebRuntime>())
        .AddSingleton<CefGlueNextWebViewFactory>(sp =>
            new CefGlueNextWebViewFactory(sp.GetRequiredService<CefGlueNextWebAppOptions>()))
        .AddSingleton<ITaruiAvaloniaWebViewFactory>(sp => sp.GetRequiredService<CefGlueNextWebViewFactory>())
        .AddSingleton<ITaruiWebViewFactory>(sp => sp.GetRequiredService<CefGlueNextWebViewFactory>())
        .AddSingleton(sp => sp.GetRequiredService<CefGlueNextWebRuntime>().Origin)
        .AddSingleton<IWebViewCookieManager, CefGlueCookieManager>();

    public static IServiceCollection AddCefGlueWebView(this IServiceCollection services, CefGlueNextWebAppOptions options) => services
        .AddSingleton(options)
        .AddSingleton(_ => new CefGlueNextWebRuntime(options))
        .AddSingleton<ITaruiWebRuntime>(sp => sp.GetRequiredService<CefGlueNextWebRuntime>())
        .AddSingleton<CefGlueNextWebViewFactory>(sp => new CefGlueNextWebViewFactory(options))
        .AddSingleton<ITaruiAvaloniaWebViewFactory>(sp => sp.GetRequiredService<CefGlueNextWebViewFactory>())
        .AddSingleton<ITaruiWebViewFactory>(sp => sp.GetRequiredService<CefGlueNextWebViewFactory>())
        .AddSingleton(sp => sp.GetRequiredService<CefGlueNextWebRuntime>().Origin)
        .AddSingleton<IWebViewCookieManager, CefGlueCookieManager>();
}
