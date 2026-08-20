using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Tarui.Shell;
using Tarui.WebView.Abstractions;
using Tarui.WebView.CefGlueNext;
using Tarui.WebView.Native;

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
            ITaruiWebViewFactory factory;
            if (string.Equals(
                    Environment.GetEnvironmentVariable("TARUI_WEBVIEW_BACKEND"),
                    "cefglue",
                    StringComparison.OrdinalIgnoreCase))
            {
                factory = new CefGlueNextWebViewFactory();
            }
            else
            {
                factory = new NativeWebViewFactory();
            }
            desktop.MainWindow = ShellBootstrap.CreateWindow(factory);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
