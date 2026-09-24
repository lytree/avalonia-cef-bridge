using Tarui.Hosting;
using Tarui.Hosting.Blazor;
using Tarui.Plugins.Autostart;
using Tarui.Plugins.Cookie;
using Tarui.Plugins.Core;
using Tarui.Plugins.DeepLink;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.Events;
using Tarui.Plugins.FileSystem;
using Tarui.Plugins.GlobalShortcut;
using Tarui.Plugins.Http;
using Tarui.Plugins.Log;
using Tarui.Plugins.Menu;
using Tarui.Plugins.Notification;
using Tarui.Plugins.Store;
using Tarui.Plugins.System;
using Tarui.Plugins.Shell;
using Tarui.Plugins.Tray;
using Tarui.Plugins.Updater;
using Tarui.Plugins.Webview;
using Tarui.Plugins.Window;
using Tarui.Plugins.WindowState;
using Tarui.Shell;
using Tarui.SingleInstance;
using Tarui.WebView.CefGlueNext;

namespace BlazorDemo;

internal static class Program
{
    private const string ApplicationId = "dev.BlazorDemo";
    private const string SingleInstanceChannel = "main";

    [STAThread]
    public static void Main(string[] args)
    {
        if (CefGlueNextAvaloniaRuntime.RunSubProcess(args))
        {
            return;
        }

        using var handle = SingleInstanceGuard.Acquire(
            new SingleInstanceIdentity(ApplicationId, SingleInstanceChannel),
            args,
            Environment.CurrentDirectory);
        if (handle.Role == InstanceRole.Secondary)
        {
            return;
        }

        var builder = TaruiHost.CreateApplicationBuilder(args);

        builder.UseApplicationIdentity(
            TaruiApplicationIdentity.FromManifest(ApplicationId, "DevBox — Blazor Hybrid 工具箱", "0.2.0"));

        builder.Services
            .AddTaruiShell()
            .AddSingleInstance(new SingleInstanceIdentity(ApplicationId, SingleInstanceChannel))
            .AddCefGlueWebView()
            .AddCorePlugin()
            .AddWindowPlugin()
            .AddWebviewPlugin()
            .AddWindowStatePlugin()
            .AddEventPlugin()
            .AddDialogPlugin()
            .AddSystemPlugin()
            .AddShellPlugin()
            .AddFileSystemPlugin()
            .AddMenuPlugin()
            .AddTrayPlugin()
            .AddNotificationPlugin()
            .AddAutostartPlugin()
            .AddGlobalShortcutPlugin()
            .AddStorePlugin()
            .AddLogPlugin()
            .AddHttpPlugin()
            .AddDeepLinkPlugin()
            .AddUpdaterPlugin()
            .AddCookiePlugin();

        // Blazor Hybrid：组件在进程内渲染，加载 tarui://localhost/index.html，无 HTTP 监听。
        builder.Services.AddTaruiBlazor(options =>
        {
            options.RootComponent = typeof(Components.App);
        });

        builder.Window.Configure(window =>
        {
            window.Title = "DevBox — Blazor Hybrid 工具箱";
            window.Width = 1280;
            window.Height = 820;
            window.MinWidth = 900;
            window.MinHeight = 600;
        });

        try
        {
            builder.Build().Run();
        }
        finally
        {
            CefGlueNextAvaloniaRuntime.Shutdown();
        }
    }
}
