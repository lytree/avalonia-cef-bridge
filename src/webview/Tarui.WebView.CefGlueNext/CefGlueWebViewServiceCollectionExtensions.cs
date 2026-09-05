using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tarui.WebView.Abstractions;
using Tarui.WebView.Avalonia;

namespace Tarui.WebView.CefGlueNext;

public static class CefGlueWebViewServiceCollectionExtensions
{
    public static IServiceCollection AddCefGlueWebView(this IServiceCollection services) => services
        .AddSingleton(sp => CefGlueNextWebAppOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()))
        .AddSingleton<CefGlueNextWebViewFactory>(sp =>
            new CefGlueNextWebViewFactory(sp.GetRequiredService<CefGlueNextWebAppOptions>()))
        .AddSingleton<ITaruiAvaloniaWebViewFactory>(sp => sp.GetRequiredService<CefGlueNextWebViewFactory>())
        .AddSingleton<ITaruiWebViewFactory>(sp => sp.GetRequiredService<CefGlueNextWebViewFactory>())
        .AddSingleton(sp => CreateAppOrigin(sp.GetRequiredService<CefGlueNextWebAppOptions>()))
        .AddSingleton<IWebViewCookieManager, CefGlueCookieManager>();

    public static IServiceCollection AddCefGlueWebView(this IServiceCollection services, CefGlueNextWebAppOptions options) => services
        .AddSingleton(options)
        .AddSingleton<CefGlueNextWebViewFactory>(_ => new CefGlueNextWebViewFactory(options))
        .AddSingleton<ITaruiAvaloniaWebViewFactory>(sp => sp.GetRequiredService<CefGlueNextWebViewFactory>())
        .AddSingleton<ITaruiWebViewFactory>(sp => sp.GetRequiredService<CefGlueNextWebViewFactory>())
        .AddSingleton(_ => CreateAppOrigin(options))
        .AddSingleton<IWebViewCookieManager, CefGlueCookieManager>();

    private static TaruiAppOrigin CreateAppOrigin(CefGlueNextWebAppOptions options) => new(
        options.StartUri,
        options.AllowedSchemes,
        options.SchemeOrigin);
}
