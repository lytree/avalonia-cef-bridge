using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using Tarui.Plugins.Window;
using Tarui.Plugins.WindowState;
using Tarui.WebView.Abstractions;

namespace Tarui.Shell;

public static class TaruiShellServiceCollectionExtensions
{
    public static IServiceCollection AddTaruiShell(this IServiceCollection services) => services
        .AddSingleton<WindowRegistry>()
        .AddSingleton<IWindowSinkRegistry>(sp => sp.GetRequiredService<WindowRegistry>())
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
        .AddSingleton<IWindowService>(sp => new AvaloniaWindowService(
            sp.GetRequiredService<WindowRegistry>(),
            (options, caller) => sp.GetRequiredService<ShellWindowFactory>().CreateEntry(options, caller)))
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
        .AddSingleton<IAutostartService, WindowsAutostartService>()
        .AddSingleton<IGlobalShortcutService>(sp => new WindowsGlobalShortcutService(sp.GetRequiredService<EventRouter>()))
        .AddSingleton<INotificationService, WindowsNotificationService>()
        .AddSingleton<DeepLinkService>(sp => new DeepLinkService(
            GetStartupArgs(),
            DeepLinkConfiguration.ReadSchemes(sp.GetService<IConfiguration>()),
            sp.GetRequiredService<EventRouter>()))
        .AddSingleton<IDeepLinkService>(sp => sp.GetRequiredService<DeepLinkService>())
        .AddSingleton<ISecondActivationSink>(sp => sp.GetRequiredService<DeepLinkService>())
        .AddHostedService<DeepLinkRegistrarHostedService>()
        .AddSingleton<IMainWindowLauncher, MainWindowLauncher>()
        .AddSingleton<IRemoteLogSink, RemoteLogSink>()
        .AddSingleton<ILoggerProvider, RemoteLoggerProvider>();

    private static string[] GetStartupArgs()
    {
        var args = Environment.GetCommandLineArgs();
        return args.Length > 1 ? args[1..] : [];
    }

    /// <summary>
    /// Builds the web view request policy from <c>Tarui:Web:Policy:*</c> configuration. The default
    /// navigation allow list confines the web view to the application origin plus local dev servers;
    /// unlisted targets default to deny. External (OS-handled) navigation defaults to <c>https:*</c>.
    /// </summary>
    private static WebViewRequestPolicy BuildWebViewRequestPolicy(
        IConfiguration? configuration,
        TaruiAppOrigin appOrigin)
    {
        var originAllow = $"{appOrigin.StartUri.Scheme}://{appOrigin.StartUri.Host}/**";

        var navAllow = configuration is null
            ? null
            : ReadList(configuration, "Tarui:Web:Policy:NavAllow");
        navAllow ??= [originAllow, "http://localhost:*/*", "http://127.0.0.1:*/*"];

        var navExternal = configuration is null
            ? null
            : ReadList(configuration, "Tarui:Web:Policy:NavExternal");
        navExternal ??= ["https:*"];

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
