using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Shell;

namespace Tarui.Hosting;

internal sealed class TaruiAvaloniaApp(IServiceProvider services) : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            services.GetRequiredService<TaruiLifetimeBridge>().Register(desktop);

            // The AppShutdownCoordinator (driven by Tarui:Application:ShutdownMode) decides when the
            // host stops. Disabling Avalonia's built-in main-window-close auto exit gives the
            // coordinator full control across OnMainWindowClose / OnLastWindowClose / Explicit.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            desktop.MainWindow = services.GetRequiredService<IMainWindowLauncher>().LaunchMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
