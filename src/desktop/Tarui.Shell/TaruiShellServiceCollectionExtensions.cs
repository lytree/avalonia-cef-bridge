using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Autostart;
using Tarui.Plugins.DeepLink;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.Events;
using Tarui.Plugins.GlobalShortcut;
using Tarui.Plugins.Log;
using Tarui.Plugins.Menu;
using Tarui.Plugins.Notification;
using Tarui.Plugins.System;
using Tarui.Plugins.Tray;
using Tarui.Plugins.Updater;
using Tarui.Plugins.Webview;
using Tarui.Plugins.Window;
using Tarui.Plugins.WindowState;
using Tarui.WebView.Abstractions;

namespace Tarui.Shell;

public static class TaruiShellServiceCollectionExtensions
{
    public static IServiceCollection AddTaruiShell(this IServiceCollection services) => services
        .AddSingleton<WindowRegistry>()
        .AddSingleton<IWindowSinkRegistry>(sp => sp.GetRequiredService<WindowRegistry>())
        .AddSingleton<WindowExtensionRegistry>(sp =>
        {
            var builder = new WindowExtensionBuilder();
            foreach (var registrar in sp.GetServices<IWindowExtensionRegistrar>())
            {
                registrar.Configure(builder);
            }

            var registrations = builder.Registrations
                .Concat(sp.GetServices<WindowExtensionRegistration>());
            return new WindowExtensionRegistry(registrations);
        })
        .AddSingleton<EventHub>()
        .AddSingleton<EventRouter>()
        .AddSingleton<WebViewRequestPolicy>(sp => BuildWebViewRequestPolicy(
            sp.GetService<IConfiguration>(),
            sp.GetRequiredService<TaruiAppOrigin>()))
        .AddSingleton<ICapabilityProvider, CapabilitySetProvider>()
        .AddSingleton<WindowCapabilityResolver>()
        .AddSingleton<ISingleInstanceCoordinator, NoopSingleInstanceCoordinator>()
        .AddSingleton<IAppShutdown, NoopAppShutdown>()
        .AddSingleton<IAppShutdownCoordinator>(sp => new AppShutdownCoordinator(
            sp.GetRequiredService<IAppShutdown>(),
            AppShutdownMode.OnMainWindowClose))
        .AddSingleton(sp => CommandRouterComposer.Compose(sp))
        .AddSingleton<IpcDispatcher>()
        .AddSingleton<IEventSender>(sp => new RoutedEventSender(sp.GetRequiredService<EventRouter>()))
        .AddSingleton<IDialogService, AvaloniaDialogService>()
        .AddSingleton<IClipboardService, AvaloniaClipboardService>()
        .AddSingleton<ShellWindowFactory>()
        .AddSingleton(WindowLifecycleOptionsFactory.Build)
        .AddSingleton<WebviewAttacher>()
        .AddSingleton<IWindowService>(sp => new AvaloniaWindowService(
            sp.GetRequiredService<WindowRegistry>(),
            (options, caller) => sp.GetRequiredService<WebviewAttacher>().Attach(options, caller),
            sp.GetRequiredService<WindowOptions>()))
        .AddSingleton<IWebviewService>(sp => new AvaloniaWebviewService(
            sp.GetRequiredService<WindowRegistry>(),
            sp.GetRequiredService<TaruiAppOrigin>()))
        .AddSingleton<IMenuService>(sp => new AvaloniaMenuService(
            sp.GetRequiredService<WindowRegistry>(),
            sp.GetRequiredService<EventRouter>()))
        .AddSingleton<ITrayService>(sp => new AvaloniaTrayService(
            sp.GetRequiredService<WindowRegistry>(),
            sp.GetRequiredService<EventRouter>()))
        .AddSingleton<IWindowStateStore>(_ => new JsonWindowStateStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "tarui.net",
                "window-state")))
        .AddSingleton<IWindowStateService, AvaloniaWindowStateService>()
        .AddSingleton<IAutostartService>(_ =>
        {
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Autostart requires the application executable path.");
            if (OperatingSystem.IsMacOS())
            {
                return new MacAutostartService(MacAutostartService.DefaultBaseDirectory(), processPath);
            }

            return OperatingSystem.IsLinux()
                ? new LinuxAutostartService(LinuxAutostartService.DefaultBaseDirectory(), processPath)
                : new WindowsAutostartService();
        })
        .AddSingleton<IGlobalShortcutService>(sp => new WindowsGlobalShortcutService(sp.GetRequiredService<EventRouter>()))
        .AddSingleton<INotificationService, WindowsNotificationService>()
        .AddSingleton<DeepLinkService>(sp => new DeepLinkService(
            GetStartupArgs(),
            DeepLinkConfiguration.ReadSchemes(sp.GetService<IConfiguration>()),
            sp.GetRequiredService<EventRouter>()))
        .AddSingleton<IDeepLinkService>(sp => sp.GetRequiredService<DeepLinkService>())
        .AddSingleton<ISecondActivationSink>(sp => sp.GetRequiredService<DeepLinkService>())
        .AddHostedService<DeepLinkRegistrarHostedService>()
        .AddSingleton<HttpClient>()
        .AddSingleton<IUpdateApplier>(_ => OperatingSystem.IsWindows()
            ? new WindowsMsixUpdateApplier()
            : new NoOpUpdateApplier())
        .AddSingleton<IUpdaterService>(sp => new UpdaterService(
            sp.GetRequiredService<HttpClient>(),
            UpdaterConfiguration.ReadSettings(sp.GetService<IConfiguration>()),
            sp.GetService<EventRouter>(),
            sp.GetService<ILogger<UpdaterService>>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdaterService>.Instance,
            sp.GetRequiredService<IUpdateApplier>()))
        .AddSingleton<IMainWindowLauncher, MainWindowLauncher>()
        .AddSingleton<IRemoteLogSink, RemoteLogSink>()
        .AddSingleton<ILoggerProvider, RemoteLoggerProvider>();

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
    /// The extension instance is created per window on the UI thread; its dependencies are resolved from
    /// the container, falling back to constructor injection for unregistered types.
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
            ? static provider => ActivatorUtilities.GetServiceOrCreateInstance<T>(provider)
            : provider => factory(provider);
        return new WindowExtensionRegistration(create, labels);
    }

    private static string[] GetStartupArgs()
    {
        var args = Environment.GetCommandLineArgs();
        return args.Length > 1 ? args[1..] : [];
    }

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
}
