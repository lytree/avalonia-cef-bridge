using Microsoft.Extensions.DependencyInjection;
using Tarui.Ipc;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.Events;
using Tarui.Plugins.System;
using Tarui.Plugins.Window;

namespace Tarui.Shell;

public static class TaruiShellServiceCollectionExtensions
{
    public static IServiceCollection AddTaruiShell(this IServiceCollection services) => services
        .AddSingleton<WindowRegistry>()
        .AddSingleton<IWindowSinkRegistry>(sp => sp.GetRequiredService<WindowRegistry>())
        .AddSingleton<EventHub>()
        .AddSingleton<EventRouter>()
        .AddSingleton<ICapabilityProvider, CapabilitySetProvider>()
        .AddSingleton(sp => CommandRouterComposer.Compose(sp))
        .AddSingleton<IpcDispatcher>()
        .AddSingleton<IEventSender>(sp => new RoutedEventSender(sp.GetRequiredService<EventRouter>()))
        .AddSingleton<IDialogService, AvaloniaDialogService>()
        .AddSingleton<IClipboardService, AvaloniaClipboardService>()
        .AddSingleton<ShellWindowFactory>()
        .AddSingleton<IWindowService>(sp => new AvaloniaWindowService(
            sp.GetRequiredService<WindowRegistry>(),
            sp.GetRequiredService<ShellWindowFactory>().CreateEntry))
        .AddSingleton<IMainWindowLauncher, MainWindowLauncher>();
}
