using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tarui.WebView.Abstractions;

namespace Tarui.WebView.CefGlueNext;

public static class CefGlueWebViewServiceCollectionExtensions
{
    public static IServiceCollection AddCefGlueWebView(this IServiceCollection services) => services
        .AddSingleton(sp => CefGlueNextWebAppOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()))
        .AddSingleton<ITaruiWebViewFactory>(sp => new CefGlueNextWebViewFactory(sp.GetRequiredService<CefGlueNextWebAppOptions>()))
        .AddSingleton(sp => new TaruiAppOrigin(sp.GetRequiredService<CefGlueNextWebAppOptions>().StartUri));

    public static IServiceCollection AddCefGlueWebView(this IServiceCollection services, CefGlueNextWebAppOptions options) => services
        .AddSingleton(options)
        .AddSingleton<ITaruiWebViewFactory>(_ => new CefGlueNextWebViewFactory(options))
        .AddSingleton(_ => new TaruiAppOrigin(options.StartUri));
}
