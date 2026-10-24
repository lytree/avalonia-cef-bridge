using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Events;
using Tarui.Plugins.System;
using Tarui.Plugins.Webview;
using Tarui.Plugins.Window;
using Tarui.WebView.Abstractions;

namespace Tarui.Shell;

public static class TaruiShellServiceCollectionExtensions
{
    public static IServiceCollection AddTaruiShell(this IServiceCollection services) => services
        .AddSingleton<WindowRegistry>()
        .AddSingleton<IWindowSinkRegistry>(sp => sp.GetRequiredService<WindowRegistry>())
        .AddSingleton<WebviewRegistry>()
        .AddSingleton<IWebviewSinkRegistry>(sp => sp.GetRequiredService<WebviewRegistry>())
        .AddSingleton<WindowExtensionRegistry>(sp =>
        {
            var builder = new WindowExtensionBuilder();
            foreach (var registrar in sp.GetServices<IWindowExtensionRegistrar>())
            {
                registrar.Configure(builder);
            }

            builder.EnsureRequiredServicesAreRegistered(sp);
            var registrations = builder.Registrations
                .Concat(sp.GetServices<WindowExtensionRegistration>());
            return new WindowExtensionRegistry(registrations);
        })
        .AddSingleton<EventHub>()
        // Window- and web view-targeted events resolve through their own registries, so both are
        // injected explicitly instead of relying on constructor-selection order.
        .AddSingleton(sp => new EventRouter(
            sp.GetRequiredService<IWindowSinkRegistry>(),
            sp.GetRequiredService<IWebviewSinkRegistry>(),
            sp.GetRequiredService<EventHub>()))
        // Native resource directory (Tauri v3 T6): packaged root, development source root and the
        // remapped-resource mirror are one concept, shared with the file system "resources" base.
        .AddSingleton(sp => BuildResourceDirectoryOptions(sp.GetService<IConfiguration>()))
        .AddSingleton<ITaruiResourceDirectory>(sp => new ResourceDirectory(
            sp.GetRequiredService<ResourceDirectoryOptions>(),
            sp.GetService<IFileAccessPolicy>()))
        .AddSingleton<WebViewRequestPolicy>(sp => BuildWebViewRequestPolicy(
            sp.GetService<IConfiguration>(),
            sp.GetRequiredService<TaruiAppOrigin>()))
        // One composition-time plugin pass shared by the router and the permission-set catalog, so a
        // capability manifest can name a set such as "core:window|default" and still be validated
        // against the exact permissions the plugins registered.
        .AddSingleton(sp => CommandRouterComposer.CreateBuilder(sp))
        .AddSingleton<IPermissionSetCatalog>(sp => new PermissionSetCatalog(
            sp.GetRequiredService<CommandRouterBuilder>().BuildPermissionSets()))
        .AddSingleton<ICapabilityProvider>(sp => new CapabilitySetProvider(
            sp.GetRequiredService<IPermissionSetCatalog>()))
        .AddSingleton<WindowCapabilityResolver>()
        .AddSingleton<ISingleInstanceCoordinator, NoopSingleInstanceCoordinator>()
        .AddSingleton<IAppShutdown, NoopAppShutdown>()
        .AddSingleton<IAppShutdownCoordinator>(sp => new AppShutdownCoordinator(
            sp.GetRequiredService<IAppShutdown>(),
            AppShutdownMode.OnMainWindowClose))
        .AddSingleton(sp => CommandRouterComposer.Compose(sp))
        .AddSingleton<IpcDispatcher>()
        .AddSingleton<IEventSender>(sp => new RoutedEventSender(sp.GetRequiredService<EventRouter>()))
        .AddSingleton<IClipboardService, AvaloniaClipboardService>()
        .AddSingleton<ShellWindowFactory>()
        .AddSingleton<WebviewAttacher>()
        .AddSingleton<IWindowService>(sp => new AvaloniaWindowService(
            sp.GetRequiredService<WindowRegistry>(),
            (options, caller) => sp.GetRequiredService<WebviewAttacher>().Attach(options, caller),
            sp.GetRequiredService<WindowOptions>()))
        .AddSingleton<IWebviewService>(sp => new AvaloniaWebviewService(
            sp.GetRequiredService<WindowRegistry>(),
            sp.GetRequiredService<WebviewRegistry>(),
            sp.GetRequiredService<WebviewAttacher>(),
            sp.GetRequiredService<TaruiAppOrigin>(),
            sp.GetRequiredService<ITaruiWebRuntime>()))
        .AddSingleton<IMainWindowLauncher, MainWindowLauncher>();

    /// <summary>
    /// Registers a plugin-style <see cref="IWindowExtensionRegistrar"/> whose <c>Configure</c> contributes
    /// native window extensions at shell composition. This is the composition-root entry point a package
    /// (or shell plugin) uses to declare several window extensions as a unit — mirroring how a plugin
    /// registers its commands.
    /// </summary>
    public static IServiceCollection AddWindowExtensionRegistrar<T>(this IServiceCollection services)
        where T : class, IWindowExtensionRegistrar =>
        services.AddSingleton<IWindowExtensionRegistrar, T>();

    /// <summary>
    /// Registers a native <see cref="IShellWindowExtension"/> that contributes controls to every window.
    /// The extension instance is created per window on the UI thread. Without a <paramref name="factory"/>
    /// the extension type is registered as a transient service and resolved through it, so construction
    /// stays explicit — there is no implicit <c>ActivatorUtilities</c> fallback.
    /// </summary>
    public static IServiceCollection AddWindowExtension<T>(
        this IServiceCollection services,
        Func<IServiceProvider, T>? factory = null)
        where T : class, IShellWindowExtension =>
        AddWindowExtensionCore(services, labels: null, factory);

    /// <summary>
    /// Registers a native <see cref="IShellWindowExtension"/> scoped to the named windows only.
    /// The extension instance is created per matching window on the UI thread.
    /// </summary>
    public static IServiceCollection AddWindowExtension<T>(
        this IServiceCollection services,
        string[] labels,
        Func<IServiceProvider, T>? factory = null)
        where T : class, IShellWindowExtension =>
        AddWindowExtensionCore(services, labels, factory);

    private static IServiceCollection AddWindowExtensionCore<T>(
        IServiceCollection services,
        string[]? labels,
        Func<IServiceProvider, T>? factory)
        where T : class, IShellWindowExtension
    {
        if (factory is null)
        {
            // A factory-free registration resolves the extension from the container, so the type must
            // be registered. Adding it here keeps the call site a single explicit declaration.
            services.TryAddTransient<T>();
        }

        services.Add(new ServiceDescriptor(
            typeof(WindowExtensionRegistration),
            _ => CreateExtensionRegistration(labels, factory),
            ServiceLifetime.Singleton));
        return services;
    }

    internal static WindowExtensionRegistration CreateExtensionRegistration<T>(
        string[]? labels,
        Func<IServiceProvider, T>? factory)
        where T : class, IShellWindowExtension
    {
        Func<IServiceProvider, IShellWindowExtension> create = factory is null
            ? static provider => ResolveRegisteredExtension<T>(provider)
            : provider => factory(provider);
        return new WindowExtensionRegistration(create, labels);
    }

    private static T ResolveRegisteredExtension<T>(IServiceProvider provider)
        where T : class, IShellWindowExtension =>
        provider.GetService<T>()
        ?? throw new InvalidOperationException(
            $"Window extension '{typeof(T).FullName}' was registered without a factory, so it must also be " +
            $"registered as a service. Call services.AddTransient<{typeof(T).Name}>() or pass a factory to " +
            "AddWindowExtension<T>()/WindowExtensionBuilder.Add<T>().");

    /// <summary>
    /// Builds the web view request policy from <c>Tarui:Web:Policy:*</c> configuration. The default
    /// navigation allow list covers every application origin — the start URI's origin plus, when local
    /// assets are served, the portless custom app scheme — and local dev servers; unlisted targets
    /// default to deny. External (OS-handled) navigation defaults to every <c>https</c> URL.
    /// </summary>
    private static WebViewRequestPolicy BuildWebViewRequestPolicy(
        IConfiguration? configuration,
        TaruiAppOrigin appOrigin)
    {
        var originAllows = new List<string>();
        foreach (var origin in new[] { appOrigin.StartUri, appOrigin.SchemeOrigin })
        {
            if (origin is null)
            {
                continue;
            }

            var pattern = origin.IsDefaultPort
                ? $"{origin.Scheme}://{origin.Host}/**"
                : $"{origin.Scheme}://{origin.Host}:{origin.Port}/**";
            if (!originAllows.Contains(pattern))
            {
                originAllows.Add(pattern);
            }
        }

        var navAllow = configuration is null
            ? null
            : ReadList(configuration, "Tarui:Web:Policy:NavAllow");
        navAllow ??= [.. originAllows, "http://localhost:*/*", "http://127.0.0.1:*/*"];

        var navExternal = configuration is null
            ? null
            : ReadList(configuration, "Tarui:Web:Policy:NavExternal");
        navExternal ??= ["https://**"];

        var downloadHosts = configuration is null
            ? null
            : ReadList(configuration, "Tarui:Web:Policy:DownloadHosts");
        downloadHosts ??= [];

        return new WebViewRequestPolicy(new WebViewPolicyOptions(
            navAllow,
            navExternal,
            downloadHosts,
            DefaultDownloadDecision: WebViewRequestDecision.Deny));
    }

    private static string[]? ReadList(IConfiguration configuration, string key)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Reads the native resource directory settings (<c>Tarui:Resources:*</c>). Unset keys stay null
    /// so <see cref="ResourceDirectory"/> can fall back to the <c>TARUI_RESOURCE_*</c> environment
    /// variables and, finally, to the packaged layout.
    /// </summary>
    private static ResourceDirectoryOptions BuildResourceDirectoryOptions(IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return new ResourceDirectoryOptions();
        }

        return new ResourceDirectoryOptions
        {
            Root = ReadOptional(configuration, "Tarui:Resources:Root"),
            DevelopmentRoot = ReadOptional(configuration, "Tarui:Resources:DevelopmentRoot"),
            MirrorDirectory = ReadOptional(configuration, "Tarui:Resources:MirrorDirectory"),
            MirroredResources = ReadList(configuration, "Tarui:Resources:Mirrored") ?? [],
        };
    }

    private static string? ReadOptional(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
