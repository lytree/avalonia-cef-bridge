using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tarui.Hosting;
using Tarui.Ipc;
using Tarui.Shell;
using Tarui.WebView.CefGlueNext;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// DI extensions that wire the Blazor Hybrid hosting package into a Tarui application.
/// </summary>
public static class TaruiBlazorServiceCollectionExtensions
{
    /// <summary>
    /// Configures the host to run Blazor Hybrid: Razor components execute in-process inside the
    /// Tarui desktop window — there is no HTTP listener. After this call:
    /// <list type="bullet">
    /// <item><see cref="ITaruiIpc"/> is registered so Blazor components can invoke Tarui commands
    ///       through the same dispatcher and capability gate the TypeScript bridge uses.</item>
    /// <item>The <c>tarui://</c> custom scheme serves the host page and static web assets
    ///       (including <c>_framework/blazor.webview.js</c>) through the official
    ///       <c>WebViewManager</c> static content pipeline.</item>
    /// <item>The main window's URL defaults to the Blazor host page when the user did not supply
    ///       one explicitly.</item>
    /// </list>
    /// The Blazor application root component type must be set on
    /// <see cref="TaruiBlazorOptions.RootComponent"/>; this keeps startup reflection-free — there
    /// is no assembly scanning to discover a default root.
    /// </summary>
    /// <remarks>
    /// Call after <c>AddCefGlueWebView()</c> so the hybrid scheme options replace the default
    /// <see cref="CefGlueNextWebAppOptions"/> registration.
    /// </remarks>
    /// <param name="services">The Tarui service collection (typically obtained via <c>TaruiHost.CreateApplicationBuilder</c>).</param>
    /// <param name="configure">Callback that mutates <see cref="TaruiBlazorOptions"/> before the host builds.</param>
    public static IServiceCollection AddTaruiBlazor(
        this IServiceCollection services,
        Action<TaruiBlazorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TaruiBlazorOptions();
        configure(options);

        services.TryAddSingleton(options);
        services.AddSingleton<ITaruiIpc, TaruiIpc>();
        services.AddSingleton<TaruiBlazorHybridState>();

        // Expose the in-process IPC façade to components as a cascading value, matching the
        // experience of the previous Blazor Server-based host.
        services.AddCascadingValue(sp => sp.GetRequiredService<ITaruiIpc>());

        // Replace the web app options so the custom scheme serves Blazor content through the
        // WebViewManager static content pipeline instead of the plain local file resolver. The
        // provider reads the manager out of TaruiBlazorHybridState lazily, so registration order
        // never matters for the scheme handler itself.
        services.AddSingleton(sp => CefGlueNextWebAppOptions.CreateScheme(
            contentRoot: options.ResolveContentRoot(),
            schemeName: options.SchemeName,
            domainName: options.DomainName,
            spaFallback: options.SpaFallback,
            contentSecurityPolicy: options.ContentSecurityPolicy,
            schemeResourceProvider: new TaruiBlazorSchemeContentProvider(
                sp.GetRequiredService<TaruiBlazorHybridState>(),
                options.ContentSecurityPolicy)));

        services.AddHostedService<TaruiBlazorHybridHostedService>();
        return services;
    }

    /// <summary>
    /// Legacy no-op retained for source compatibility: the window URL now falls back to the
    /// application origin's start URI (the Blazor host page) automatically.
    /// </summary>
    public static IServiceCollection UseTaruiBlazorWindow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
