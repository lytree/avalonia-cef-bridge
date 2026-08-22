using Tarui.Hosting;
using Tarui.Plugins.Autostart;
using Tarui.Plugins.Core;
using Tarui.Plugins.DeepLink;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.Events;
using Tarui.Plugins.FileSystem;
using Tarui.Plugins.GlobalShortcut;
using Tarui.Plugins.Menu;
using Tarui.Plugins.Notification;
using Tarui.Plugins.Store;
using Tarui.Plugins.Log;
using Tarui.Plugins.System;
using Tarui.Plugins.Tray;
using Tarui.Plugins.Updater;
using Tarui.Plugins.Window;
using Tarui.Plugins.WindowState;
using Tarui.Shell;
using Tarui.SingleInstance;
using Tarui.WebView.CefGlueNext;

namespace Tarui.App;

internal static class Program
{
    private const string ApplicationId = "tarui.net";
    private const string SingleInstanceChannel = "main";

    [STAThread]
    public static void Main(string[] args)
    {
        CefGlueRuntimeBootstrap.RunSubProcess(args);

        // Second instances forward their arguments to the running primary and exit before the host
        // is ever built. The primary holds the instance lock for the rest of its lifetime.
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
            .AddWindowStatePlugin()
            .AddEventPlugin()
            .AddDialogPlugin()
            .AddSystemPlugin()
            .AddFileSystemPlugin()
            .AddMenuPlugin()
            .AddTrayPlugin()
            .AddNotificationPlugin()
            .AddAutostartPlugin()
            .AddGlobalShortcutPlugin()
            .AddStorePlugin()
            .AddLogPlugin()
            .AddDeepLinkPlugin()
            .AddUpdaterPlugin();

        builder.Window.Configure(window =>
        {
            window.Title = "tarui.net";
            window.Width = 1280;
            window.Height = 820;
        });

        builder.Build().Run();
    }
}
