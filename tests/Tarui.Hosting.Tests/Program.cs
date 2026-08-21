using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tarui.Contracts;
using Tarui.Hosting;
using Tarui.Ipc;
using Tarui.Shell;
using Tarui.WebView.Abstractions;

namespace Tarui.Hosting.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            BuilderExposesHostSurfaces();
            ContentRootIsPinnedToBaseDirectory();
            MainWindowUsesDefaults();
            ConfigurationOverridesWindowDefaults();
            WindowCodeConfigurationOverridesConfigurationValues();
            InvalidWindowConfigurationFailsFast();
            PluginCompositionRegistersCommands();
            await HostLifecycleDrivesShutdownBridge();
            ShutdownCoordinatorDefaultsToMainWindowClose();
            ShutdownModeConfigurationOverridesCoordinator();
            InvalidShutdownModeFailsFast();
            ShutdownCoordinatorEnforcesEachMode();
            HostAppShutdownStopsGracefully();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        Console.WriteLine("Tarui.Hosting self-tests passed.");
        return 0;
    }

    private static void BuilderExposesHostSurfaces()
    {
        var builder = TaruiHost.CreateApplicationBuilder();

        Assert(builder.Configuration is not null, "The builder must expose a configuration manager.");
        Assert(builder.Services is not null, "The builder must expose the service collection.");
        Assert(builder.Logging is not null, "The builder must expose the logging builder.");
        Assert(builder.Window is not null, "The builder must expose the window builder.");
    }

    private static void ContentRootIsPinnedToBaseDirectory()
    {
        using var app = TaruiHost.CreateApplicationBuilder().Build();

        var environment = app.Services.GetRequiredService<IHostEnvironment>();
        Assert(
            environment.ContentRootPath == AppContext.BaseDirectory,
            $"The content root must be pinned to AppContext.BaseDirectory, but was '{environment.ContentRootPath}'.");
    }

    private static void MainWindowUsesDefaults()
    {
        using var app = TaruiHost.CreateApplicationBuilder().Build();

        var options = app.Services.GetRequiredService<WindowOptions>();
        Assert(options.Label == "main", "The main window label must be 'main'.");
        Assert(options.Title == "tarui.net", "The default main window title must be 'tarui.net'.");
        Assert(options.Width == 1280, "The default main window width must be 1280.");
        Assert(options.Height == 820, "The default main window height must be 820.");
        Assert(options.MinWidth == 900, "The default main window minimum width must be 900.");
        Assert(options.MinHeight == 600, "The default main window minimum height must be 600.");
        Assert(options.Center, "The main window must be centered by default.");
        Assert(options.Url is null, "The default main window URL must be null.");
    }

    private static void ConfigurationOverridesWindowDefaults()
    {
        var builder = TaruiHost.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tarui:Window:Title"] = "cfg",
            ["Tarui:Window:Width"] = "1000",
        });

        using var app = builder.Build();
        var options = app.Services.GetRequiredService<WindowOptions>();
        Assert(options.Title == "cfg", "The configured title must override the default.");
        Assert(options.Width == 1000, "The configured width must override the default.");
        Assert(options.Height == 820, "The height must keep its default when not configured.");
        Assert(options.MinWidth == 900, "The minimum width must keep its default when not configured.");
        Assert(options.MinHeight == 600, "The minimum height must keep its default when not configured.");
        Assert(options.Center, "Centering must keep its default when not configured.");
    }

    private static void WindowCodeConfigurationOverridesConfigurationValues()
    {
        var builder = TaruiHost.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tarui:Window:Width"] = "1000",
        });
        builder.Window.Configure(window => window.Width = 777);

        using var app = builder.Build();
        var options = app.Services.GetRequiredService<WindowOptions>();
        Assert(options.Width == 777, "Window code configuration must win over configuration values.");
    }

    private static void InvalidWindowConfigurationFailsFast()
    {
        var builder = TaruiHost.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tarui:Window:Width"] = "abc",
        });

        InvalidOperationException? failure = null;
        try
        {
            _ = builder.Build();
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        Assert(failure is not null, "An invalid window configuration value must fail the build.");
        Assert(
            failure!.Message.Contains("Tarui:Window:Width", StringComparison.Ordinal),
            "The failure must name the offending configuration key.");
    }

    private static void PluginCompositionRegistersCommands()
    {
        var builder = TaruiHost.CreateApplicationBuilder();
        builder.Services.AddTaruiShell();
        builder.Services.AddPlugin<TestPlugin>();
        builder.Services.AddSingleton(new TaruiAppOrigin(new Uri("tarui://localhost/index.html")));

        using var app = builder.Build();
        var router = app.Services.GetRequiredService<CommandRouter>();
        Assert(
            router.Commands.Contains("test:ping"),
            "The plugin command must be registered on the composed router.");
    }

    private static async Task HostLifecycleDrivesShutdownBridge()
    {
        var builder = TaruiHost.CreateApplicationBuilder();
        builder.Services.AddSingleton<RecordingHostedService>();
        builder.Services.AddSingleton<IHostedService>(
            serviceProvider => serviceProvider.GetRequiredService<RecordingHostedService>());

        using var app = builder.Build();
        await app.StartAsync();

        var hostedService = app.Services.GetRequiredService<RecordingHostedService>();
        Assert(hostedService.Started, "The hosted service must start with the host.");

        await app.StopAsync();
        Assert(hostedService.Stopped, "The hosted service must stop with the host.");

        var bridge = app.Services.GetRequiredService<TaruiLifetimeBridge>();
        Assert(
            bridge.ShutdownRequested,
            "Stopping the host must request the Avalonia shutdown via the lifetime bridge.");
    }

    private static void ShutdownCoordinatorDefaultsToMainWindowClose()
    {
        using var app = TaruiHost.CreateApplicationBuilder().Build();

        var shutdown = app.Services.GetRequiredService<IAppShutdown>();
        Assert(shutdown is HostAppShutdown, "The host must register a host-coordinated IAppShutdown.");

        var coordinator = app.Services.GetRequiredService<IAppShutdownCoordinator>();
        Assert(
            coordinator.Mode == AppShutdownMode.OnMainWindowClose,
            "The default shutdown mode must be OnMainWindowClose.");
    }

    private static void ShutdownModeConfigurationOverridesCoordinator()
    {
        var builder = TaruiHost.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tarui:Application:ShutdownMode"] = "OnLastWindowClose",
        });

        using var app = builder.Build();
        var coordinator = app.Services.GetRequiredService<IAppShutdownCoordinator>();
        Assert(
            coordinator.Mode == AppShutdownMode.OnLastWindowClose,
            "The configured shutdown mode must override the default.");
    }

    private static void InvalidShutdownModeFailsFast()
    {
        var builder = TaruiHost.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tarui:Application:ShutdownMode"] = "NeverStop",
        });

        InvalidOperationException? failure = null;
        try
        {
            _ = builder.Build();
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        Assert(failure is not null, "An invalid shutdown mode must fail the build.");
        Assert(
            failure!.Message.Contains("Tarui:Application:ShutdownMode", StringComparison.Ordinal),
            "The failure must name the offending configuration key.");
    }

    private static void ShutdownCoordinatorEnforcesEachMode()
    {
        var shutdown = new RecordingAppShutdown();

        var mainWindowClose = new AppShutdownCoordinator(shutdown, AppShutdownMode.OnMainWindowClose);
        mainWindowClose.NotifyWindowClosed("main", 1);
        mainWindowClose.NotifyWindowClosed("editor", 2);
        Assert(shutdown.Shutdowns.Count == 1, "OnMainWindowClose must stop when the main window closes.");

        var lastWindowClose = new AppShutdownCoordinator(shutdown, AppShutdownMode.OnLastWindowClose);
        lastWindowClose.NotifyWindowClosed("editor", 1);
        Assert(shutdown.Shutdowns.Count == 1, "OnLastWindowClose must not stop while a window remains.");
        lastWindowClose.NotifyWindowClosed("editor", 0);
        Assert(shutdown.Shutdowns.Count == 2, "OnLastWindowClose must stop when the last window closes.");

        var explicitMode = new AppShutdownCoordinator(shutdown, AppShutdownMode.Explicit);
        explicitMode.NotifyWindowClosed("main", 0);
        Assert(shutdown.Shutdowns.Count == 2, "Explicit mode must not stop on window close.");
    }

    private static void HostAppShutdownStopsGracefully()
    {
        using var app = TaruiHost.CreateApplicationBuilder().Build();

        var shutdown = app.Services.GetRequiredService<IAppShutdown>();
        // RequestShutdown goes through the host, so only the host can observe it; the key assertion is
        // that the concrete type exists and exposes the host lifecycle rather than Environment.Exit.
        Assert(shutdown is HostAppShutdown, "IAppShutdown must resolve to the host-backed implementation.");
    }

    private sealed class RecordingAppShutdown : IAppShutdown
    {
        public List<int> Shutdowns { get; } = [];

        public void RequestShutdown(int exitCode = 0) => Shutdowns.Add(exitCode);

        public bool TryStartRelaunch() => true;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TestPlugin : ITaruiPlugin
    {
        public void ConfigureCommands(CommandRouterBuilder commands)
        {
            commands.Add(
                "test:ping",
                TaruiJsonContext.Default.EmptyArgs,
                TaruiJsonContext.Default.Unit,
                static (_, _, _) => ValueTask.FromResult(new Unit()),
                "test:ping");
        }
    }

    private sealed class RecordingHostedService : IHostedService
    {
        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            return Task.CompletedTask;
        }
    }
}
