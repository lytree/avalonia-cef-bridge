using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tarui.Contracts;
using Tarui.Hosting;
using Tarui.Ipc;
using Tarui.Shell;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// DI extensions that wire the Blazor hosting package into a Tarui application.
/// </summary>
public static class TaruiBlazorServiceCollectionExtensions
{
    /// <summary>
    /// Configures the host to host a Blazor Server application. After this call:
    /// <list type="bullet">
    /// <item><see cref="ITaruiIpc"/> is registered so Blazor components can invoke Tarui commands.</item>
    /// <item>An embedded ASP.NET Core <see cref="IHost"/> starts before the main window opens and
    ///       exposes its absolute URL through <see cref="TaruiBlazorServer.StartUri"/>.</item>
    /// <item>The main window's <see cref="TaruiWindowBuilder.Url"/> is automatically set to the
    ///       Blazor root URL when the user did not provide one explicitly.</item>
    /// </list>
    /// The Blazor application root component type must be set on
    /// <see cref="TaruiBlazorOptions.RootComponent"/> before the host builds; this keeps startup
    /// reflection-free — there is no assembly scanning to discover a default root.
    /// </summary>
    /// <param name="services">The Tarui service collection (typically obtained via <c>TaruiHost.CreateApplicationBuilder</c>).</param>
    /// <param name="configure">Callback that mutates <see cref="TaruiBlazorOptions"/> before the embedded server starts.</param>
    public static IServiceCollection AddTaruiBlazor(
        this IServiceCollection services,
        Action<TaruiBlazorOptions> configure)
        => services.AddTaruiBlazor(configure, configureApp: null);

    /// <summary>
    /// Variant of <see cref="AddTaruiBlazor(IServiceCollection, Action{TaruiBlazorOptions})"/> that
    /// also lets the host application configure the embedded <see cref="WebApplication"/>. Useful when
    /// the host wants to register additional Razor endpoints or middleware on the same server.
    /// </summary>
    public static IServiceCollection AddTaruiBlazor(
        this IServiceCollection services,
        Action<TaruiBlazorOptions> configure,
        Action<WebApplication>? configureApp)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TaruiBlazorOptions();
        configure(options);

        services.TryAddSingleton(options);
        services.AddSingleton<ITaruiIpc, TaruiIpc>();
        services.AddSingleton<TaruiBlazorServer>(sp =>
        {
            var opts = sp.GetRequiredService<TaruiBlazorOptions>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<TaruiBlazorServer>();
            var host = TaruiBlazorHostFactory.Create(opts, configureApp);
            return new TaruiBlazorServer(host, opts, logger);
        });

        services.AddHostedService<TaruiBlazorHostedService>();
        return services;
    }

    /// <summary>
    /// Sets the Blazor root URL on the host's main <see cref="TaruiWindowBuilder"/>. Idempotent and
    /// safe to call after <see cref="AddTaruiBlazor(IServiceCollection, Action{TaruiBlazorOptions})"/>.
    /// </summary>
    public static IServiceCollection UseTaruiBlazorWindow(this IServiceCollection services)
    {
        services.AddHostedService<TaruiBlazorWindowUrlApplier>();
        return services;
    }
}

/// <summary>
/// Reads <see cref="TaruiBlazorOptions"/> from configuration under the <c>Tarui:Blazor:*</c> section.
/// </summary>
internal static class TaruiBlazorConfigurationBinder
{
    public static void BindFromConfiguration(TaruiBlazorOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("Tarui:Blazor");
        if (!section.Exists())
        {
            return;
        }

        var port = section["Port"];
        if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var parsedPort))
        {
            options.Port = parsedPort;
        }

        var host = section["Host"];
        if (!string.IsNullOrWhiteSpace(host))
        {
            options.Host = host;
        }

        var rootPath = section["RootPath"];
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            options.RootPath = rootPath;
        }
    }
}