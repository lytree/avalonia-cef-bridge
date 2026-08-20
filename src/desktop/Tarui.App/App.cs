using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Tarui.Shell;
using Tarui.WebView.CefGlueNext;

namespace Tarui.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var webAppOptions = CefGlueNextWebAppOptions.FromEnvironment();
            var webViewFactory = new CefGlueNextWebViewFactory(webAppOptions);
            desktop.MainWindow = ShellBootstrap.CreateWindow(
                webViewFactory,
                webAppOptions.StartUri);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
