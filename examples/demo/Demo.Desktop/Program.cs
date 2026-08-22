using Tarui.Hosting;
using Tarui.Plugins.Core;
using Tarui.Plugins.Events;
using Tarui.Plugins.FileSystem;
using Tarui.Plugins.Store;
using Tarui.Plugins.Window;
using Tarui.Shell;
using Tarui.SingleInstance;
using Tarui.WebView.CefGlueNext;

namespace Demo;

internal static class Program
{
    private const string ApplicationId = "dev.Demo";
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
            .AddWindowPlugin()
            .AddEventPlugin()
            .AddStorePlugin()
            .AddFileSystemPlugin();

        // Optional: demonstrate a native push event once the window is live.
        // Configuring an app:// event here would require wiring EventNames.ValidateWebEmit
        // against a host-side producer; the frontend demo uses window events instead.
        builder.Window.Configure(window =>
        {
            window.Title = "Tarui Demo";
            window.Width = 1280;
            window.Height = 820;
        });

        builder.Build().Run();
    }
}