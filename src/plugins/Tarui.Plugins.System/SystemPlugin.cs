using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.System;

public sealed class SystemPlugin(
    IPathService pathService,
    IOsService osService,
    IProcessService processService,
    IShellService shellService,
    IClipboardService clipboardService) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new SystemCommands(pathService, osService, processService, shellService, clipboardService);

        commands.Add(
            "core:path|resolve",
            TaruiJsonContext.Default.PathResolveOptions,
            TaruiJsonContext.Default.PathResolveResult,
            handlers.ResolvePathAsync,
            "core:path|resolve");

        commands.Add(
            "core:os|info",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.OsInfo,
            handlers.GetOsInfoAsync,
            "core:os|info");

        commands.Add(
            "core:platform|capabilities",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.PlatformCapabilities,
            SystemCommands.GetPlatformCapabilitiesAsync,
            "core:platform|capabilities");

        commands.Add(
            "core:process|exit",
            TaruiJsonContext.Default.ProcessExitOptions,
            TaruiJsonContext.Default.Unit,
            handlers.ExitAsync,
            "core:process|exit");

        commands.Add(
            "core:process|relaunch",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            handlers.RelaunchAsync,
            "core:process|relaunch");

        commands.Add(
            "core:shell|open",
            TaruiJsonContext.Default.ShellOpenOptions,
            TaruiJsonContext.Default.ShellOpenResult,
            handlers.OpenAsync,
            "core:shell|open");

        commands.Add(
            "core:clipboard|read-text",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.ClipboardReadTextResult,
            handlers.ReadClipboardAsync,
            "core:clipboard|read-text");

        commands.Add(
            "core:clipboard|write-text",
            TaruiJsonContext.Default.ClipboardWriteTextOptions,
            TaruiJsonContext.Default.Unit,
            handlers.WriteClipboardAsync,
            "core:clipboard|write-text");
    }

    private sealed class SystemCommands(
        IPathService pathService,
        IOsService osService,
        IProcessService processService,
        IShellService shellService,
        IClipboardService clipboardService)
    {
        [TaruiCommand("core:path|resolve")]
        public ValueTask<PathResolveResult> ResolvePathAsync(
            PathResolveOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PathResolveResult(pathService.Resolve(options.Kind, options.Path)));
        }

        [TaruiCommand("core:os|info")]
        public ValueTask<OsInfo> GetOsInfoAsync(
            EmptyArgs options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(osService.GetInfo());
        }

        [TaruiCommand("core:platform|capabilities")]
        public static ValueTask<PlatformCapabilities> GetPlatformCapabilitiesAsync(
            EmptyArgs options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PlatformCapabilityProvider.Get());
        }

        [TaruiCommand("core:process|exit")]
        public ValueTask<Unit> ExitAsync(
            ProcessExitOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processService.Shutdown(options.Code);
            return ValueTask.FromResult(new Unit());
        }

        [TaruiCommand("core:process|relaunch")]
        public ValueTask<Unit> RelaunchAsync(
            EmptyArgs options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processService.Relaunch();
            return ValueTask.FromResult(new Unit());
        }

        [TaruiCommand("core:shell|open")]
        public ValueTask<ShellOpenResult> OpenAsync(
            ShellOpenOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(shellService.Open(options.Target));
        }

        [TaruiCommand("core:clipboard|read-text")]
        public async ValueTask<ClipboardReadTextResult> ReadClipboardAsync(
            EmptyArgs options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            var text = await clipboardService.ReadTextAsync(cancellationToken);
            return new ClipboardReadTextResult(text);
        }

        [TaruiCommand("core:clipboard|write-text")]
        public async ValueTask<Unit> WriteClipboardAsync(
            ClipboardWriteTextOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            await clipboardService.WriteTextAsync(options.Text, cancellationToken);
            return new Unit();
        }
    }
}

/// <summary>
/// Reports which OS-coupled features are genuinely implemented on the current platform. This is derived purely
/// from the running OS so the web layer can disable UI rather than rely on a runtime degraded no-op. Flags reflect
/// the actual footprint today: autostart has Windows/macOS/Linux backends; notification and global-shortcut only
/// Windows; deep-link has Windows + Linux registrars (macOS acceptance pending).
/// </summary>
public static class PlatformCapabilityProvider
{
    public static PlatformCapabilities Get()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new PlatformCapabilities(
                NotificationSupported: false,
                NotificationReason: "macos-notifications-pending",
                GlobalShortcutSupported: false,
                GlobalShortcutReason: "macos-global-shortcut-pending",
                AutostartSupported: true,
                DeepLinkSupported: false,
                DeepLinkReason: "macos-deeplink-pending");
        }

        if (OperatingSystem.IsLinux())
        {
            return new PlatformCapabilities(
                NotificationSupported: false,
                NotificationReason: "linux-notifications-pending",
                GlobalShortcutSupported: false,
                GlobalShortcutReason: "linux-global-shortcut-pending",
                AutostartSupported: true,
                DeepLinkSupported: true,
                DeepLinkReason: null);
        }

        return new PlatformCapabilities(
            NotificationSupported: true,
            NotificationReason: null,
            GlobalShortcutSupported: true,
            GlobalShortcutReason: null,
            AutostartSupported: true,
            DeepLinkSupported: true,
            DeepLinkReason: null);
    }
}

public static class SystemPluginServiceCollectionExtensions
{
    public static IServiceCollection AddSystemPlugin(this IServiceCollection services) => services
        .AddSingleton<IPathService, PathService>()
        .AddSingleton<IOsService, OsService>()
        .AddSingleton<IAppShutdown, NoopAppShutdown>()
        .AddSingleton<IProcessService, ProcessService>()
        .AddSingleton<IShellService, ShellService>()
        .AddPlugin<SystemPlugin>();
}
