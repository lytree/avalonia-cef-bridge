using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Core;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.Events;
using Tarui.Plugins.System;
using Tarui.Plugins.Webview;
using Tarui.Plugins.Window;

namespace Tarui.Plugins.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        await RegistersWindowCommandsWithPermissions();
        await DispatchesWindowCreateWithOptions();
        await FallsBackToContextWindowLabel();
        await ReturnsWindowStateAndLabels();
        await EmitsEventsThroughEventPlugin();
        await ResolvesPathsThroughSystemPlugin();
        await ReadsAndWritesClipboard();
        await ExitsAndRelaunchesThroughProcessCommands();
        await OpensShellTargetsAndReportsOsInfo();
        await OpensDialogsForRequestingWindow();
        await ShowsMessageBoxForRequestingWindow();
        await ConfirmsForRequestingWindow();
        await DeniesCommandsOutsideCapability();
        RegistersWebviewCommandsWithPermissions();
        await NavigatesOwnWebviewWithoutOtherPermission();
        await DeniesOtherWebviewWithoutPermission();
        await AllowsOtherWebviewWithPermission();
        await ReturnsWebviewStateAndLabels();
        ResolvesCorePluginThroughServiceCollection();
        ResolvesWindowPluginThroughServiceCollection();
        ResolvesEventPluginThroughServiceCollection();
        ResolvesDialogPluginThroughServiceCollection();
        ResolvesSystemPluginThroughServiceCollection();
        ResolvesWebviewPluginThroughServiceCollection();
        Console.WriteLine("Tarui.Plugins self-tests passed.");
        return 0;
    }

    private static async Task RegistersWindowCommandsWithPermissions()
    {
        var builder = new CommandRouterBuilder();
        new WindowPlugin(new FakeWindowService()).ConfigureCommands(builder);

        var expected = new[]
        {
            "core:window|create",
            "core:window|close",
            "core:window|minimize",
            "core:window|maximize",
            "core:window|unmaximize",
            "core:window|toggle-maximize",
            "core:window|hide",
            "core:window|show",
            "core:window|focus",
            "core:window|center",
            "core:window|set-title",
            "core:window|set-size",
            "core:window|set-position",
            "core:window|set-min-size",
            "core:window|set-max-size",
            "core:window|set-always-on-top",
            "core:window|set-resizable",
            "core:window|set-decorations",
            "core:window|set-fullscreen",
            "core:window|get-state",
            "core:window|current-monitor",
            "core:window|primary-monitor",
            "core:window|monitors",
            "core:window|list",
        };
        var router = builder.Build();
        Assert(
            expected.All(router.Commands.Contains),
            "Every window command must be routed.");
        Assert(
            expected.All(permission => builder.RegisteredPermissions.Contains(permission)),
            "Every window command must register its permission.");
    }

    private static async Task DispatchesWindowCreateWithOptions()
    {
        var service = new FakeWindowService();
        var router = BuildRouter(service);
        var response = await router.InvokeAsync(
            Request(
                "core:window|create",
                new WindowOptions("editor") { Title = "Editor", Width = 640, Height = 480 },
                TaruiJsonContext.Default.WindowOptions),
            AllowAll());

        Assert(response.Success, "Window creation must succeed.");
        Assert(
            service.Calls.Contains("create|editor|Editor|640x480"),
            "The service must receive the deserialized window options.");
    }

    private static async Task FallsBackToContextWindowLabel()
    {
        var service = new FakeWindowService();
        var router = BuildRouter(service);
        var response = await router.InvokeAsync(
            Request("core:window|minimize", new WindowLabelOptions(), TaruiJsonContext.Default.WindowLabelOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["*"])));

        Assert(response.Success, "Minimize must succeed.");
        Assert(
            service.Calls.Contains("minimize|editor"),
            "A missing label must fall back to the context window label.");
    }

    private static async Task ReturnsWindowStateAndLabels()
    {
        var service = new FakeWindowService
        {
            Labels = ["main", "editor"],
            Monitors =
            [
                new MonitorInfo(
                    "Primary",
                    new LogicalPosition(0, 0),
                    new LogicalSize(1920, 1080),
                    new LogicalPosition(0, 0),
                    new LogicalSize(1920, 1040),
                    1.25,
                    IsPrimary: true,
                    IsCurrent: true),
            ],
        };
        var router = BuildRouter(service);

        var stateResponse = await router.InvokeAsync(
            Request("core:window|get-state", new WindowLabelOptions("editor"), TaruiJsonContext.Default.WindowLabelOptions),
            AllowAll());
        var state = Result(stateResponse, TaruiJsonContext.Default.WindowStateInfo);
        Assert(state.Label == "editor", "The state must be scoped to the requested window.");
        Assert(state.Title == "main-title", "The state must carry the window title.");

        var listResponse = await router.InvokeAsync(
            Request("core:window|list", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            AllowAll());
        var labels = Result(listResponse, TaruiJsonContext.Default.WindowLabels);
        Assert(
            labels.Labels.SequenceEqual(["main", "editor"]),
            "The label list must match the service response.");

        var monitorsResponse = await router.InvokeAsync(
            Request("core:window|monitors", new WindowLabelOptions("main"), TaruiJsonContext.Default.WindowLabelOptions),
            AllowAll());
        var monitors = Result(monitorsResponse, TaruiJsonContext.Default.MonitorInfoArray);
        Assert(monitors.Length == 1 && monitors[0].ScaleFactor == 1.25, "Monitor details must round-trip.");
    }

    private static async Task EmitsEventsThroughEventPlugin()
    {
        var sender = new FakeEventSender();
        var router = BuildRouter(sender);
        var payload = JsonSerializer.SerializeToElement(new ThemeChanged("dark"), TaruiJsonContext.Default.ThemeChanged);
        var response = await router.InvokeAsync(
            Request(
                "core:event|emit",
                new EventEmitOptions("user://theme-changed", payload, "editor"),
                TaruiJsonContext.Default.EventEmitOptions),
            AllowAll());

        Assert(response.Success, "Event emission must succeed.");
        Assert(sender.Emitted.Count == 1, "The sender must receive exactly one event.");
        Assert(
            sender.Emitted[0].Event == "user://theme-changed" && sender.Emitted[0].TargetWindow == "editor",
            "The event name and target window must round-trip.");
        Assert(
            sender.Emitted[0].Payload.GetProperty("theme").GetString() == "dark",
            "The event payload must round-trip.");
    }

    private static async Task ResolvesPathsThroughSystemPlugin()
    {
        var services = new FakeSystemServices();
        var router = BuildRouter(services);
        var response = await router.InvokeAsync(
            Request(
                "core:path|resolve",
                new PathResolveOptions("appdata", "cache/db.sqlite"),
                TaruiJsonContext.Default.PathResolveOptions),
            AllowAll());

        var result = Result(response, TaruiJsonContext.Default.PathResolveResult);
        Assert(
            result.Path == "/resolved/appdata/cache/db.sqlite",
            "Path resolution must delegate to the path service.");
    }

    private static async Task ReadsAndWritesClipboard()
    {
        var services = new FakeSystemServices();
        var router = BuildRouter(services);
        var writeResponse = await router.InvokeAsync(
            Request(
                "core:clipboard|write-text",
                new ClipboardWriteTextOptions("hello tarui"),
                TaruiJsonContext.Default.ClipboardWriteTextOptions),
            AllowAll());
        Assert(writeResponse.Success, "Clipboard writes must succeed.");

        var readResponse = await router.InvokeAsync(
            Request("core:clipboard|read-text", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            AllowAll());
        var read = Result(readResponse, TaruiJsonContext.Default.ClipboardReadTextResult);
        Assert(
            read.Text == "hello tarui",
            "Clipboard reads must return the previously written text.");
    }

    private static async Task ExitsAndRelaunchesThroughProcessCommands()
    {
        var services = new FakeSystemServices();
        var router = BuildRouter(services);
        var exitResponse = await router.InvokeAsync(
            Request("core:process|exit", new ProcessExitOptions(3), TaruiJsonContext.Default.ProcessExitOptions),
            AllowAll());
        Assert(exitResponse.Success, "Process exit must succeed.");

        var relaunchResponse = await router.InvokeAsync(
            Request("core:process|relaunch", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            AllowAll());
        Assert(relaunchResponse.Success, "Process relaunch must succeed.");
        Assert(
            services.Process.Calls.SequenceEqual(["shutdown:3", "relaunch"]),
            "The process service must observe exit and relaunch calls.");
    }

    private static async Task OpensShellTargetsAndReportsOsInfo()
    {
        var services = new FakeSystemServices();
        var router = BuildRouter(services);
        var shellResponse = await router.InvokeAsync(
            Request("core:shell|open", new ShellOpenOptions("https://example.com"), TaruiJsonContext.Default.ShellOpenOptions),
            AllowAll());
        var shell = Result(shellResponse, TaruiJsonContext.Default.ShellOpenResult);
        Assert(shell.Opened, "Shell opens must report success.");

        var osResponse = await router.InvokeAsync(
            Request("core:os|info", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            AllowAll());
        var os = Result(osResponse, TaruiJsonContext.Default.OsInfo);
        Assert(os.Platform == "windows", "OS info must round-trip from the OS service.");
    }

    private static async Task OpensDialogsForRequestingWindow()
    {
        var dialog = new FakeDialogService();
        var router = BuildRouter(dialog);
        var openResponse = await router.InvokeAsync(
            Request(
                "plugin:dialog|open",
                new OpenDialogOptions(Multiple: false, Directory: false),
                TaruiJsonContext.Default.OpenDialogOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:dialog|open"])));
        var opened = Result(openResponse, TaruiJsonContext.Default.OpenDialogResult);
        Assert(
            opened.Paths.SequenceEqual(["C:/tmp/a.txt"]),
            "Dialog open results must round-trip.");
        Assert(
            dialog.WindowLabels.Contains("editor"),
            "Dialogs must run against the requesting window.");

        var saveResponse = await router.InvokeAsync(
            Request(
                "plugin:dialog|save",
                new SaveDialogOptions("notes.txt"),
                TaruiJsonContext.Default.SaveDialogOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:dialog|open", "plugin:dialog|save"])));
        var saved = Result(saveResponse, TaruiJsonContext.Default.SaveDialogResult);
        Assert(saved.Path == "C:/tmp/notes.txt", "Dialog save results must round-trip.");
    }

    private static async Task ShowsMessageBoxForRequestingWindow()
    {
        var dialog = new FakeDialogService();
        var router = BuildRouter(dialog);
        var response = await router.InvokeAsync(
            Request(
                "plugin:dialog|message",
                new MessageBoxOptions(
                    "Save changes?",
                    "This change cannot be undone.",
                    MessageBoxIconNames.Warning,
                    MessageBoxButtonNames.OkCancel),
                TaruiJsonContext.Default.MessageBoxOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:dialog|message"])));

        Assert(response.Success, "Message box must succeed.");
        var result = Result(response, TaruiJsonContext.Default.MessageBoxResult);
        Assert(result.Result == MessageBoxResultNames.Ok, "Message box results must round-trip.");
        Assert(
            dialog.WindowLabels.Contains("editor"),
            "Message boxes must run against the requesting window.");
        Assert(
            dialog.Messages.Contains((MessageBoxIconNames.Warning, MessageBoxButtonNames.OkCancel)),
            "Message box options must round-trip.");
    }

    private static async Task ConfirmsForRequestingWindow()
    {
        var dialog = new FakeDialogService();
        var router = BuildRouter(dialog);
        var response = await router.InvokeAsync(
            Request(
                "plugin:dialog|confirm",
                new ConfirmOptions("Delete file?", "This action cannot be undone."),
                TaruiJsonContext.Default.ConfirmOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:dialog|confirm"])));

        Assert(response.Success, "Confirm must succeed.");
        var result = Result(response, TaruiJsonContext.Default.ConfirmResult);
        Assert(result.Confirmed, "Confirm results must round-trip.");
        Assert(
            dialog.Confirms.Contains("Delete file?|question|OK|Cancel"),
            "Confirm options must round-trip with their defaults.");
    }

    private static async Task DeniesCommandsOutsideCapability()
    {
        var router = BuildRouter(new FakeWindowService());
        var response = await router.InvokeAsync(
            Request("core:window|list", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            new CommandContext("main", "main", new CapabilitySet([])));

        Assert(!response.Success, "A command outside the capability must fail.");
        Assert(response.Error?.Code == "PERMISSION_DENIED", "The error must be PERMISSION_DENIED.");
    }

    private static void RegistersWebviewCommandsWithPermissions()
    {
        var builder = new CommandRouterBuilder();
        new WebviewPlugin(new FakeWebviewService()).ConfigureCommands(builder);

        var expected = new[]
        {
            "plugin:webview|navigate",
            "plugin:webview|get-state",
            "plugin:webview|list",
        };
        var router = builder.Build();
        Assert(
            expected.All(router.Commands.Contains),
            "Every webview command must be routed.");
        Assert(
            expected.All(builder.RegisteredPermissions.Contains),
            "Every webview command must register its permission.");
        Assert(
            builder.RegisteredPermissions.Contains("plugin:webview|navigate-other-webview") &&
            builder.RegisteredPermissions.Contains("plugin:webview|get-state-other-webview"),
            "The other-webview permission variants must be registered.");
    }

    private static async Task NavigatesOwnWebviewWithoutOtherPermission()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);
        var response = await router.InvokeAsync(
            Request(
                "plugin:webview|navigate",
                new WebviewNavigateOptions("tarui://page"),
                TaruiJsonContext.Default.WebviewNavigateOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|navigate"])));

        Assert(response.Success, "Navigating the caller's own webview must succeed.");
        Assert(
            service.Calls.Contains("navigate|editor|tarui://page"),
            "A missing label must fall back to the context webview label.");
    }

    private static async Task DeniesOtherWebviewWithoutPermission()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);
        var response = await router.InvokeAsync(
            Request(
                "plugin:webview|navigate",
                new WebviewNavigateOptions("tarui://page", "main"),
                TaruiJsonContext.Default.WebviewNavigateOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|navigate"])));

        Assert(!response.Success, "Addressing another webview without the -other-webview permission must fail.");
        Assert(response.Error?.Code == "PERMISSION_DENIED", "The error must be PERMISSION_DENIED.");
    }

    private static async Task AllowsOtherWebviewWithPermission()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);
        var response = await router.InvokeAsync(
            Request(
                "plugin:webview|get-state",
                new WebviewLabelOptions("main"),
                TaruiJsonContext.Default.WebviewLabelOptions),
            new CommandContext(
                "editor",
                "editor",
                new CapabilitySet(["plugin:webview|get-state", "plugin:webview|get-state-other-webview"])));

        Assert(response.Success, "Addressing another webview with the -other-webview permission must succeed.");
        Assert(
            service.Calls.Contains("get-state|main"),
            "The target webview label must be passed to the service.");
    }

    private static async Task ReturnsWebviewStateAndLabels()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);
        var stateResponse = await router.InvokeAsync(
            Request(
                "plugin:webview|get-state",
                new WebviewLabelOptions("editor"),
                TaruiJsonContext.Default.WebviewLabelOptions),
            AllowAll());
        var state = Result(stateResponse, TaruiJsonContext.Default.WebviewStateInfo);
        Assert(state.Label == "editor", "The state must be scoped to the requested webview.");
        Assert(state.Url == "editor://start", "The state must carry the webview URL.");
        Assert(state.WindowLabel == "editor-window", "The state must expose the host window label.");

        var listResponse = await router.InvokeAsync(
            Request("plugin:webview|list", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            AllowAll());
        var labels = Result(listResponse, TaruiJsonContext.Default.WebviewLabels);
        Assert(
            labels.Labels.SequenceEqual(["main", "editor"]),
            "The label list must match the service response.");
    }

    private static void ResolvesWebviewPluginThroughServiceCollection()
    {
        using var provider = new ServiceCollection()
            .AddWebviewPlugin()
            .AddSingleton<IWebviewService>(new FakeWebviewService())
            .BuildServiceProvider();

        Assert(
            provider.GetRequiredService<ITaruiPlugin>() is WebviewPlugin,
            "AddWebviewPlugin must resolve the webview plugin.");
    }

    private static CommandRouter BuildRouter(FakeWebviewService service)
    {
        var builder = new CommandRouterBuilder();
        new WebviewPlugin(service).ConfigureCommands(builder);
        return builder.Build();
    }

    private static CommandRouter BuildRouter(FakeWindowService service)
    {
        var builder = new CommandRouterBuilder();
        new WindowPlugin(service).ConfigureCommands(builder);
        return builder.Build();
    }

    private static CommandRouter BuildRouter(FakeEventSender sender)
    {
        var builder = new CommandRouterBuilder();
        new EventPlugin(sender).ConfigureCommands(builder);
        return builder.Build();
    }

    private static CommandRouter BuildRouter(FakeSystemServices services)
    {
        var builder = new CommandRouterBuilder();
        new SystemPlugin(services.Paths, services.Os, services.Process, services.Shell, services.Clipboard)
            .ConfigureCommands(builder);
        return builder.Build();
    }

    private static CommandRouter BuildRouter(FakeDialogService dialog)
    {
        var builder = new CommandRouterBuilder();
        new DialogPlugin(dialog).ConfigureCommands(builder);
        return builder.Build();
    }

    private static void ResolvesCorePluginThroughServiceCollection()
    {
        using var provider = new ServiceCollection()
            .AddCorePlugin()
            .BuildServiceProvider();

        Assert(
            provider.GetRequiredService<ITaruiPlugin>() is CorePlugin,
            "AddCorePlugin must resolve the core plugin.");
    }

    private static void ResolvesWindowPluginThroughServiceCollection()
    {
        using var provider = new ServiceCollection()
            .AddWindowPlugin()
            .AddSingleton<IWindowService>(new FakeWindowService())
            .BuildServiceProvider();

        Assert(
            provider.GetRequiredService<ITaruiPlugin>() is WindowPlugin,
            "AddWindowPlugin must resolve the window plugin.");
    }

    private static void ResolvesEventPluginThroughServiceCollection()
    {
        using var provider = new ServiceCollection()
            .AddEventPlugin()
            .AddSingleton<IEventSender>(new FakeEventSender())
            .BuildServiceProvider();

        Assert(
            provider.GetRequiredService<ITaruiPlugin>() is EventPlugin,
            "AddEventPlugin must resolve the event plugin.");
    }

    private static void ResolvesDialogPluginThroughServiceCollection()
    {
        using var provider = new ServiceCollection()
            .AddDialogPlugin()
            .AddSingleton<IDialogService>(new FakeDialogService())
            .BuildServiceProvider();

        Assert(
            provider.GetRequiredService<ITaruiPlugin>() is DialogPlugin,
            "AddDialogPlugin must resolve the dialog plugin.");
    }

    private static void ResolvesSystemPluginThroughServiceCollection()
    {
        using var provider = new ServiceCollection()
            .AddSystemPlugin()
            .AddSingleton<IClipboardService>(new FakeClipboardService())
            .BuildServiceProvider();

        Assert(
            provider.GetRequiredService<ITaruiPlugin>() is SystemPlugin,
            "AddSystemPlugin must resolve the system plugin.");
        Assert(
            provider.GetRequiredService<IPathService>() is PathService,
            "AddSystemPlugin must register the path service.");
        Assert(
            provider.GetRequiredService<IOsService>() is OsService,
            "AddSystemPlugin must register the OS service.");
        Assert(
            provider.GetRequiredService<IProcessService>() is ProcessService,
            "AddSystemPlugin must register the process service.");
        Assert(
            provider.GetRequiredService<IShellService>() is ShellService,
            "AddSystemPlugin must register the shell service.");
    }

    private static CommandContext AllowAll() => new("main", "main", new CapabilitySet(["*"]));

    private static InvokeRequest Request<T>(string command, T payload, JsonTypeInfo<T> payloadType) =>
        new(1, $"t-{Guid.NewGuid():N}", command, JsonSerializer.SerializeToElement(payload, payloadType));

    private static T Result<T>(InvokeResponse response, JsonTypeInfo<T> resultType) =>
        response.Payload is { } payload
            ? payload.Deserialize(resultType) ?? throw new InvalidOperationException("The result payload is null.")
            : throw new InvalidOperationException("The response has no payload.");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
