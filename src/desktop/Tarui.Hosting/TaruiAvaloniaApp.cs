using Avalonia;
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
            desktop.MainWindow = services.GetRequiredService<IMainWindowLauncher>().LaunchMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
