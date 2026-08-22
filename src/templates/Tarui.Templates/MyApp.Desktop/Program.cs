using Tarui.Hosting;
using Tarui.Plugins.Core;
using Tarui.Plugins.Window;
using Tarui.Shell;
using Tarui.SingleInstance;
using Tarui.WebView.CefGlueNext;

namespace MyApp;

internal static class Program
{
    private const string ApplicationId = "dev.MyApp";
    private const string SingleInstanceChannel = "main";

    [STAThread]
    public static void Main(string[] args)
    {
        CefGlueRuntimeBootstrap.RunSubProcess(args);

        // Second instances forward their arguments to the running primary and exit
        // before the host is ever built.
        using var handle = SingleInstanceGuard.Acquire(
            new SingleInstanceIdentity(ApplicationId, SingleInstanceChannel),
            args,
            Environment.CurrentDirectory);
        if (handle.Role == InstanceRole.Secondary)
        {
            return;
        }

        var builder = TaruiHost.CreateApplicationBuilder(args);

        builder.Services
            .AddTaruiShell()
            .AddSingleInstance(new SingleInstanceIdentity(ApplicationId, SingleInstanceChannel))
            .AddCefGlueWebView()
            .AddCorePlugin()
            .AddWindowPlugin();

        builder.Window.Configure(window =>
        {
            window.Title = "MyApp";
            window.Width = 1280;
            window.Height = 820;
        });

        builder.Build().Run();
    }
}