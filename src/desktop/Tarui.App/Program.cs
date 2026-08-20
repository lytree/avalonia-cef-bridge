using Tarui.Hosting;
using Tarui.Plugins.Core;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.Events;
using Tarui.Plugins.System;
using Tarui.Plugins.Window;
using Tarui.Shell;
using Tarui.WebView.CefGlueNext;

namespace Tarui.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CefGlueRuntimeBootstrap.RunSubProcess(args);

        var builder = TaruiHost.CreateApplicationBuilder(args);

        builder.Services
            .AddTaruiShell()
            .AddCefGlueWebView()
            .AddCorePlugin()
            .AddWindowPlugin()
            .AddEventPlugin()
            .AddDialogPlugin()
            .AddSystemPlugin();

        builder.Window.Configure(window =>
        {
            window.Title = "tarui.net";
            window.Width = 1280;
            window.Height = 820;
        });

        builder.Build().Run();
    }
}
