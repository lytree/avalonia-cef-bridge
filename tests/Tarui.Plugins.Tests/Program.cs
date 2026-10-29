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
        await DispatchesWindowCreateWithTransparencyOptions();
        await FallsBackToContextWindowLabel();
        await SetsWindowIconAndTheme();
        await SetsWindowEffectsThroughCommands();
        await FullscreensOnMonitorContainingPoint();
        await ReturnsWindowStateAndLabels();
        await EmitsEventsThroughEventPlugin();
        await ResolvesPathsThroughSystemPlugin();
        ResolvesOverriddenAppDirectoriesThroughPathService();
        await ReadsAndWritesClipboard();
        await ReadsAndWritesClipboardHtmlAndImage();
        ParsesStructuredCliArguments();
        CliParsingRejectsInvalidInput();
        await ExitsAndRelaunchesThroughProcessCommands();
        await OpensShellTargetsAndReportsOsInfo();
        await ReportsPlatformCapabilities();
        await OpensDialogsForRequestingWindow();
        await ShowsMessageBoxForRequestingWindow();
        await ConfirmsForRequestingWindow();
        await AsksForRequestingWindow();
        await DeniesCommandsOutsideCapability();
        RegistersWebviewCommandsWithPermissions();
        await NavigatesOwnWebviewWithoutOtherPermission();
        await DeniesOtherWebviewWithoutPermission();
        await AllowsOtherWebviewWithPermission();
        await TogglesDevToolsForOwnAndOtherWebview();
        await ReturnsWebviewStateAndLabels();
        await ReportsWebviewRuntimeInfo();
        await CreatesWebviewInsideCallerWindow();
        await DeniesCrossWindowCreateWithoutPermission();
        await SetsAndClearsWebviewBoundsViaCommand();
        await DestroysWebviewThroughCommand();
        await RoutesSendDevToolsMessageToService();
        await DeniesSendDevToolsMessageWithoutPermission();
        await RejectsEmptyDevToolsMessage();
        await EvaluatesScriptsThroughWebviewCommands();
        await CompletesEvalCallbackOnBoundChannel();
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
            "core:window|set-always-on-bottom",
            "core:window|set-shadow",
            "core:window|set-badge-count",
            "core:window|set-progress-bar",
            "core:window|set-icon",
            "core:window|set-theme",
            "core:window|set-resizable",
            "core:window|set-decorations",
            "core:window|set-fullscreen",
            "core:window|set-fullscreen-on-monitor",
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

    private static async Task DispatchesWindowCreateWithTransparencyOptions()
    {
        var service = new FakeWindowService();
        var router = BuildRouter(service);
        var response = await router.InvokeAsync(
            Request(
                "core:window|create",
                new WindowOptions("palette")
                {
                    Title = "Palette",
                    Width = 420,
                    Height = 300,
                    Transparent = true,
                },
                TaruiJsonContext.Default.WindowOptions),
            AllowAll());

        Assert(response.Success, "Creating a transparent window must succeed.");
        Assert(
            service.Calls.Contains("create|palette|Palette|420x300|transparent"),
            "The service must receive the transparency option.");
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

    private static async Task SetsWindowIconAndTheme()
    {
        var service = new FakeWindowService();
        var router = BuildRouter(service);

        var iconResponse = await router.InvokeAsync(
            Request(
                "core:window|set-icon",
                new SetIconOptions([137, 80]),
                TaruiJsonContext.Default.SetIconOptions),
            AllowAll());
        Assert(iconResponse.Success, "Setting a window icon must succeed.");
        Assert(
            service.Calls.Contains("set-icon|main|png:2"),
            "The icon bytes must reach the service with the caller window label.");

        var appIconResponse = await router.InvokeAsync(
            Request(
                "core:window|set-icon",
                new SetIconOptions(FromAppIcon: true),
                TaruiJsonContext.Default.SetIconOptions),
            AllowAll());
        Assert(appIconResponse.Success, "Restoring the app icon must succeed.");
        Assert(
            service.Calls.Contains("set-icon|main|app-icon"),
            "The app-icon flag must reach the service instead of PNG bytes.");

        var themeResponse = await router.InvokeAsync(
            Request(
                "core:window|set-theme",
                new SetThemeOptions("dark"),
                TaruiJsonContext.Default.SetThemeOptions),
            AllowAll());
        Assert(themeResponse.Success, "Setting the window theme must succeed.");
        Assert(
            service.Calls.Contains("set-theme|main|dark"),
            "The theme must reach the service with the caller window label.");
    }

    private static async Task SetsWindowEffectsThroughCommands()
    {
        var service = new FakeWindowService();
        var router = BuildRouter(service);

        var bottom = await router.InvokeAsync(
            Request(
                "core:window|set-always-on-bottom",
                new SetFlagOptions(true),
                TaruiJsonContext.Default.SetFlagOptions),
            AllowAll());
        Assert(bottom.Success, "Setting always-on-bottom must succeed.");
        Assert(
            service.Calls.Contains("set-always-on-bottom|main|True"),
            "The bottom z-order flag must reach the service with the caller window label.");

        var shadow = await router.InvokeAsync(
            Request(
                "core:window|set-shadow",
                new SetFlagOptions(false),
                TaruiJsonContext.Default.SetFlagOptions),
            AllowAll());
        Assert(shadow.Success, "Setting the frame shadow must succeed.");
        Assert(
            service.Calls.Contains("set-shadow|main|False"),
            "The shadow flag must reach the service with the caller window label.");

        var badge = await router.InvokeAsync(
            Request(
                "core:window|set-badge-count",
                new SetBadgeCountOptions(7),
                TaruiJsonContext.Default.SetBadgeCountOptions),
            AllowAll());
        Assert(badge.Success, "Setting the badge count must succeed.");
        Assert(
            service.Calls.Contains("set-badge-count|main|7"),
            "The badge count must reach the service.");

        var clear = await router.InvokeAsync(
            Request(
                "core:window|set-badge-count",
                new SetBadgeCountOptions(null),
                TaruiJsonContext.Default.SetBadgeCountOptions),
            AllowAll());
        Assert(clear.Success, "Clearing the badge count must succeed.");
        Assert(
            service.Calls.Contains("set-badge-count|main|clear"),
            "A null badge count must clear the badge.");

        var progress = await router.InvokeAsync(
            Request(
                "core:window|set-progress-bar",
                new SetProgressBarOptions("paused", 42.5),
                TaruiJsonContext.Default.SetProgressBarOptions),
            AllowAll());
        Assert(progress.Success, "Setting the progress bar must succeed.");
        Assert(
            service.Calls.Contains("set-progress-bar|main|paused|42.5"),
            "The progress status and percentage must reach the service.");

        var denied = await router.InvokeAsync(
            Request(
                "core:window|set-badge-count",
                new SetBadgeCountOptions(3, "editor"),
                TaruiJsonContext.Default.SetBadgeCountOptions),
            new CommandContext("main", "main", new CapabilitySet(["core:window|set-badge-count"])));
        Assert(!denied.Success, "Cross-window badge updates must require the -other-window permission.");
    }

    private static async Task FullscreensOnMonitorContainingPoint()
    {
        var service = new FakeWindowService();
        var router = BuildRouter(service);

        var response = await router.InvokeAsync(
            Request(
                "core:window|set-fullscreen-on-monitor",
                new SetFullscreenOnMonitorOptions(2500, 300),
                TaruiJsonContext.Default.SetFullscreenOnMonitorOptions),
            AllowAll());
        Assert(response.Success, "Fullscreening on the monitor containing a point must succeed.");
        Assert(
            service.Calls.Contains("set-fullscreen-on-monitor|main|2500,300"),
            "The physical point must reach the service with the caller window label.");

        var denied = await router.InvokeAsync(
            Request(
                "core:window|set-fullscreen-on-monitor",
                new SetFullscreenOnMonitorOptions(0, 0, "editor"),
                TaruiJsonContext.Default.SetFullscreenOnMonitorOptions),
            new CommandContext("main", "main", new CapabilitySet(["core:window|set-fullscreen-on-monitor"])));
        Assert(
            !denied.Success,
            "Cross-window fullscreen-on-monitor must require the -other-window permission.");
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

    private static void ResolvesOverriddenAppDirectoriesThroughPathService()
    {
        // appDirectoriesOverride 必须同样作用于 core:path|resolve，而不只是 fs/store 的授权基准。
        var root = Path.Combine(Path.GetTempPath(), "tarui-path-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new PathService(new AppDirectoryOptions { AppLog = root });
            Assert(
                service.Resolve("appLog", null) == Path.GetFullPath(root),
                "The overridden appLog base must be returned by the path service.");
            Assert(
                service.Resolve("appLog", "app.log") == Path.Combine(Path.GetFullPath(root), "app.log"),
                "Relative paths must resolve under the override, not the platform default.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    private static async Task ReadsAndWritesClipboardHtmlAndImage()
    {
        var services = new FakeSystemServices();
        var router = BuildRouter(services);

        var htmlResponse = await router.InvokeAsync(
            Request(
                "core:clipboard|write-html",
                new ClipboardWriteHtmlOptions("<b>hi</b>", "hi"),
                TaruiJsonContext.Default.ClipboardWriteHtmlOptions),
            AllowAll());
        Assert(htmlResponse.Success, "HTML clipboard writes must succeed.");
        Assert(
            services.Clipboard.Html == "<b>hi</b>" && services.Clipboard.Text == "hi",
            "Writing HTML must store the HTML and the plain-text fallback.");

        var htmlReadResponse = await router.InvokeAsync(
            Request("core:clipboard|read-html", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            AllowAll());
        var htmlRead = Result(htmlReadResponse, TaruiJsonContext.Default.ClipboardReadHtmlResult);
        Assert(htmlRead.Available && htmlRead.Html == "<b>hi</b>", "Reading HTML must return what was written.");

        byte[] png = [137, 80, 78, 71];
        var imageResponse = await router.InvokeAsync(
            Request(
                "core:clipboard|write-image",
                new ClipboardWriteImageOptions(png),
                TaruiJsonContext.Default.ClipboardWriteImageOptions),
            AllowAll());
        Assert(imageResponse.Success, "Image clipboard writes must succeed.");
        Assert(services.Clipboard.Png!.SequenceEqual(png), "Writing an image must reach the clipboard bytes.");

        var imageReadResponse = await router.InvokeAsync(
            Request("core:clipboard|read-image", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            AllowAll());
        var imageRead = Result(imageReadResponse, TaruiJsonContext.Default.ClipboardReadImageResult);
        Assert(imageRead.Available && imageRead.Png!.SequenceEqual(png), "Reading an image must return the PNG bytes.");
    }

    private static void ParsesStructuredCliArguments()
    {
        var options = new CliParseOptions(
            [
                new CliArgSpec("verbose", "v", CliArgKind.Flag),
                new CliArgSpec("name", "n", CliArgKind.Text, Required: true),
                new CliArgSpec("count", "c", CliArgKind.Text),
                new CliArgSpec("tag", Kind: CliArgKind.TextList, Multiple: true),
                new CliArgSpec("level", Kind: CliArgKind.Number),
            ],
            PositionalName: "input",
            Args: ["--verbose", "-n", "demo", "--count=3", "--tag", "a", "--tag", "b", "--level", "9", "file.txt"]);

        var result = CliParser.Parse(options);
        Assert(result.Success, "A well-formed CLI invocation must parse successfully.");
        var values = result.Values.ToDictionary(static value => value.Name);
        Assert(values["verbose"].Present && values["verbose"].Kind == CliArgKind.Flag,
            "A flag must parse as present.");
        Assert(values["name"].Value == "demo", "A long option must capture its value.");
        Assert(values["count"].Value == "3", "Inline --name=value must parse.");
        Assert(values["tag"]!.Values!.SequenceEqual(["a", "b"]), "A repeated multi option must collect all values.");
        Assert(values["level"].Number == 9, "A number option must parse to a 64-bit value.");
        Assert(result.Positionals.SequenceEqual(["file.txt"]), "Bare arguments must become positionals.");
    }

    private static void CliParsingRejectsInvalidInput()
    {
        var requiredMissing = CliParser.Parse(new CliParseOptions(
            [new CliArgSpec("name", Kind: CliArgKind.Text, Required: true)],
            Args: []));
        Assert(!requiredMissing.Success && requiredMissing.Error!.Contains("required"),
            "A missing required option must fail the parse.");

        var badInteger = CliParser.Parse(new CliParseOptions(
            [new CliArgSpec("level", Kind: CliArgKind.Number)],
            Args: ["--level", "nine"]));
        Assert(!badInteger.Success && badInteger.Error!.Contains("integer"),
            "A malformed typed value must fail the parse.");

        var unknown = CliParser.Parse(new CliParseOptions(
            [new CliArgSpec("known", Kind: CliArgKind.Text)],
            Args: ["--wat"]));
        Assert(!unknown.Success && unknown.Error!.Contains("Unknown option '--wat'"),
            "An unknown long option must fail the parse.");

        var positionalRequired = CliParser.Parse(new CliParseOptions(
            [new CliArgSpec("flag")],
            PositionalName: "input",
            PositionalRequired: true,
            Args: ["--flag"]));
        Assert(!positionalRequired.Success, "A required positional that is absent must fail the parse.");
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

    private static async Task ReportsPlatformCapabilities()
    {
        var router = BuildRouter(new FakeSystemServices());
        var response = await router.InvokeAsync(
            Request("core:platform|capabilities", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            AllowAll());
        var caps = Result(response, TaruiJsonContext.Default.PlatformCapabilities);
        Assert(caps.AutostartSupported, "Autostart must be reported as supported on the current platform.");
        Assert(
            caps.NotificationSupported == OperatingSystem.IsWindows(),
            "Notification availability must honestly reflect the current OS footprint.");
        Assert(
            caps.GlobalShortcutSupported == OperatingSystem.IsWindows(),
            "Global-shortcut availability must honestly reflect the current OS footprint.");
        Assert(
            caps.DeepLinkSupported == (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()),
            "Deep-link availability must honestly reflect the current OS footprint.");
        Assert(
            caps.WindowEffectsSupported == OperatingSystem.IsWindows(),
            "Window effects must be reported as Windows-only.");
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

    private static async Task AsksForRequestingWindow()
    {
        var dialog = new FakeDialogService();
        var router = BuildRouter(dialog);
        var response = await router.InvokeAsync(
            Request(
                "plugin:dialog|ask",
                new AskOptions("Keep changes?", "Unsaved edits will be lost.", CancelLabel: "Cancel"),
                TaruiJsonContext.Default.AskOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:dialog|ask"])));

        Assert(response.Success, "Ask must succeed.");
        var result = Result(response, TaruiJsonContext.Default.AskResult);
        Assert(result.Answer == true, "Ask results must round-trip (Yes => true).");
        Assert(
            dialog.WindowLabels.Contains("editor"),
            "Ask dialogs must run against the requesting window.");
        Assert(
            dialog.Asks.Contains("Keep changes?|question|Yes|No|Cancel"),
            "Ask options must round-trip with their defaults and cancel label.");
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
            "plugin:webview|devtools",
            "plugin:webview|eval",
            "plugin:webview|eval-with-callback",
            "plugin:webview|eval-callback-complete",
            "plugin:webview|list",
            "plugin:webview|runtime-info",
            "plugin:webview|create",
            "plugin:webview|set-bounds",
            "plugin:webview|destroy",
            "plugin:webview|send-devtools-message",
        };
        // The internal completion command deliberately reuses the eval-with-callback permission id,
        // so it is routed as a command but never registered as its own permission.
        var expectedPermissions = expected.Where(command => command != "plugin:webview|eval-callback-complete");
        var router = builder.Build();
        Assert(
            expected.All(router.Commands.Contains),
            "Every webview command must be routed.");
        Assert(
            expectedPermissions.All(builder.RegisteredPermissions.Contains),
            "Every webview command must register its permission.");
        Assert(
            builder.RegisteredPermissions.Contains("plugin:webview|navigate-other-webview") &&
            builder.RegisteredPermissions.Contains("plugin:webview|get-state-other-webview") &&
            builder.RegisteredPermissions.Contains("plugin:webview|devtools-other-webview") &&
            builder.RegisteredPermissions.Contains("plugin:webview|eval-other-webview") &&
            builder.RegisteredPermissions.Contains("plugin:webview|eval-with-callback-other-webview"),
            "The other-webview permission variants must be registered.");
        Assert(
            builder.RegisteredPermissions.Contains("plugin:webview|set-bounds-other-webview") &&
            builder.RegisteredPermissions.Contains("plugin:webview|destroy-other-webview") &&
            builder.RegisteredPermissions.Contains("plugin:webview|send-devtools-message-other-webview") &&
            builder.RegisteredPermissions.Contains("plugin:webview|create-other-window"),
            "The cross-surface and cross-window permission variants must be registered.");
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

    private static async Task TogglesDevToolsForOwnAndOtherWebview()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);

        var own = await router.InvokeAsync(
            Request(
                "plugin:webview|devtools",
                new WebviewDevToolsOptions(Open: true),
                TaruiJsonContext.Default.WebviewDevToolsOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|devtools"])));
        Assert(own.Success, "Opening devtools on the caller's own webview must succeed.");
        Assert(service.Calls.Contains("devtools|editor|open"), "The open state must reach the service.");

        var other = await router.InvokeAsync(
            Request(
                "plugin:webview|devtools",
                new WebviewDevToolsOptions(Open: false, Label: "main"),
                TaruiJsonContext.Default.WebviewDevToolsOptions),
            new CommandContext(
                "editor",
                "editor",
                new CapabilitySet(["plugin:webview|devtools", "plugin:webview|devtools-other-webview"])));
        Assert(other.Success, "Closing devtools on another webview requires the -other-webview permission.");
        Assert(service.Calls.Contains("devtools|main|close"), "The close state must reach the target webview.");

        var denied = await router.InvokeAsync(
            Request(
                "plugin:webview|devtools",
                new WebviewDevToolsOptions(Open: true, Label: "main"),
                TaruiJsonContext.Default.WebviewDevToolsOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|devtools"])));
        Assert(!denied.Success, "Addressing another webview without the -other-webview permission must fail.");
    }

    private static async Task EvaluatesScriptsThroughWebviewCommands()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);

        var own = await router.InvokeAsync(
            Request(
                "plugin:webview|eval",
                new WebviewEvalOptions("1 + 1"),
                TaruiJsonContext.Default.WebviewEvalOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|eval"])));
        Assert(own.Success, "Evaluating a script on the caller's own webview must succeed.");
        Assert(
            service.Calls.Contains("eval|editor|1 + 1"),
            "The script must reach the caller's own webview.");

        var withCallback = await router.InvokeAsync(
            Request(
                "plugin:webview|eval-with-callback",
                new WebviewEvalCallbackOptions("2 + 2", OnEvent: "chan-7"),
                TaruiJsonContext.Default.WebviewEvalCallbackOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|eval-with-callback"])));
        Assert(withCallback.Success, "Starting a callback eval on the caller's own webview must succeed.");
        Assert(
            service.Calls.Contains("eval-with-callback|editor|chan-7|2 + 2"),
            "The callback channel token must reach the service.");

        var denied = await router.InvokeAsync(
            Request(
                "plugin:webview|eval",
                new WebviewEvalOptions("1", Label: "main"),
                TaruiJsonContext.Default.WebviewEvalOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|eval"])));
        Assert(!denied.Success, "Evaluating on another webview without the -other-webview permission must fail.");
        Assert(denied.Error?.Code == "PERMISSION_DENIED", "The error must be PERMISSION_DENIED.");
    }

    private static async Task CompletesEvalCallbackOnBoundChannel()
    {
        var sink = new RecordingChannelSink();
        var builder = new CommandRouterBuilder();
        new WebviewPlugin(new FakeWebviewService()).ConfigureCommands(builder);
        var router = builder.Build();

        var previous = ChannelSinkContext.Current;
        ChannelSinkContext.Current = sink;
        try
        {
            var response = await router.InvokeAsync(
                Request(
                    "plugin:webview|eval-callback-complete",
                    new WebviewEvalCompleteOptions("chan-42", Ok: true, Value: "42"),
                    TaruiJsonContext.Default.WebviewEvalCompleteOptions),
                new CommandContext("main", "main", new CapabilitySet(["plugin:webview|eval-with-callback"])));
            Assert(response.Success, "Completing an eval callback must succeed.");
        }
        finally
        {
            ChannelSinkContext.Current = previous;
        }

        var frame = sink.Frames.Single();
        Assert(frame.Id == "chan-42", "The completion frame must address the caller's channel token.");
        var payload = frame.Payload.Deserialize(TaruiJsonContext.Default.WebviewEvalFrame);
        Assert(
            payload is { Ok: true, Value: "42", Error: null },
            "The completion frame must carry the eval outcome.");
    }

    private sealed class RecordingChannelSink : IChannelSink
    {
        public List<(string Id, JsonElement Payload)> Frames { get; } = [];

        public ValueTask SendAsync(string channelId, JsonElement payload, CancellationToken cancellationToken = default)
        {
            Frames.Add((channelId, payload));
            return ValueTask.CompletedTask;
        }
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

    private static async Task ReportsWebviewRuntimeInfo()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);

        var response = await router.InvokeAsync(
            Request("plugin:webview|runtime-info", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            new CommandContext("main", "main", new CapabilitySet(["plugin:webview|runtime-info"])));
        Assert(response.Success, "Reporting the runtime identity must succeed when permitted.");
        Assert(service.Calls.Contains("runtime-info"), "The runtime-info command must reach the service.");

        var info = Result(response, TaruiJsonContext.Default.WebviewRuntimeInfo);
        Assert(info.Name == "cef", "The runtime name must come from the service.");
        Assert(info.WebviewVersion == "150.0.11", "The engine version must come from the service.");
        Assert(info.ResourceMode == "scheme", "The resource mode must come from the service.");
        Assert(info.Origin == "tarui://localhost/", "The application origin must come from the service.");

        var denied = await router.InvokeAsync(
            Request("plugin:webview|runtime-info", new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
            new CommandContext("main", "main", new CapabilitySet(["plugin:webview|list"])));
        Assert(!denied.Success, "Runtime info must be denied without its own permission.");
        Assert(denied.Error?.Code == "PERMISSION_DENIED", "The error must be PERMISSION_DENIED.");
    }

    private static async Task CreatesWebviewInsideCallerWindow()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);

        var response = await router.InvokeAsync(
            Request(
                "plugin:webview|create",
                new WebviewCreateOptions("side", Url: "tarui://side"),
                TaruiJsonContext.Default.WebviewCreateOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|create"])));

        Assert(response.Success, "Creating a web view in the caller's own window must succeed.");
        Assert(
            service.Calls.Contains("create|editor|side"),
            "A missing window label must fall back to the caller's window.");

        var state = Result(response, TaruiJsonContext.Default.WebviewStateInfo);
        Assert(state.Label == "side", "The created state must report the new web view label.");

        var denied = await router.InvokeAsync(
            Request(
                "plugin:webview|create",
                new WebviewCreateOptions("another"),
                TaruiJsonContext.Default.WebviewCreateOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|list"])));
        Assert(!denied.Success, "Creating a web view must be denied without its permission.");
        Assert(denied.Error?.Code == "PERMISSION_DENIED", "The error must be PERMISSION_DENIED.");
    }

    private static async Task DeniesCrossWindowCreateWithoutPermission()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);

        var denied = await router.InvokeAsync(
            Request(
                "plugin:webview|create",
                new WebviewCreateOptions("side", "main"),
                TaruiJsonContext.Default.WebviewCreateOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|create"])));
        Assert(!denied.Success, "Attaching to another window without -other-window must fail.");
        Assert(denied.Error?.Code == "PERMISSION_DENIED", "The error must be PERMISSION_DENIED.");
        Assert(
            !service.Calls.Any(call => call.StartsWith("create|", StringComparison.Ordinal)),
            "A denied create must never reach the service.");

        var allowed = await router.InvokeAsync(
            Request(
                "plugin:webview|create",
                new WebviewCreateOptions("side", "main"),
                TaruiJsonContext.Default.WebviewCreateOptions),
            new CommandContext(
                "editor",
                "editor",
                new CapabilitySet(["plugin:webview|create", "plugin:webview|create-other-window"])));
        Assert(allowed.Success, "Attaching to another window must succeed with -other-window.");
        Assert(service.Calls.Contains("create|main|side"), "The resolved window label must reach the service.");
    }

    private static async Task SetsAndClearsWebviewBoundsViaCommand()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);

        var withBounds = await router.InvokeAsync(
            Request(
                "plugin:webview|set-bounds",
                new WebviewSetBoundsOptions(new WebviewBounds(10, 20, 300, 200)),
                TaruiJsonContext.Default.WebviewSetBoundsOptions),
            new CommandContext("main", "main", new CapabilitySet(["plugin:webview|set-bounds"])));
        Assert(withBounds.Success, "Setting bounds on the caller's own web view must succeed.");
        Assert(service.Calls.Contains("set-bounds|main|10,20,300,200"), "The bounds must reach the service.");

        var cleared = await router.InvokeAsync(
            Request(
                "plugin:webview|set-bounds",
                new WebviewSetBoundsOptions(null),
                TaruiJsonContext.Default.WebviewSetBoundsOptions),
            new CommandContext("main", "main", new CapabilitySet(["plugin:webview|set-bounds"])));
        Assert(cleared.Success, "Clearing bounds must succeed.");
        Assert(service.Calls.Contains("set-bounds|main|fill"), "Clearing bounds must request window fill.");
    }

    private static async Task DestroysWebviewThroughCommand()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);

        var response = await router.InvokeAsync(
            Request(
                "plugin:webview|destroy",
                new WebviewLabelOptions(),
                TaruiJsonContext.Default.WebviewLabelOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|destroy"])));
        Assert(response.Success, "Destroying the caller's own web view must succeed.");
        Assert(
            service.Calls.Contains("destroy|editor"),
            "A missing label must fall back to the caller's web view label.");

        var denied = await router.InvokeAsync(
            Request(
                "plugin:webview|destroy",
                new WebviewLabelOptions("main"),
                TaruiJsonContext.Default.WebviewLabelOptions),
            new CommandContext("editor", "editor", new CapabilitySet(["plugin:webview|destroy"])));
        Assert(!denied.Success, "Destroying another web view without -other-webview must fail.");
        Assert(denied.Error?.Code == "PERMISSION_DENIED", "The error must be PERMISSION_DENIED.");
    }

    private static async Task RoutesSendDevToolsMessageToService()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);

        var response = await router.InvokeAsync(
            Request(
                "plugin:webview|send-devtools-message",
                new WebviewDevToolsMessageOptions("{\"id\":1,\"method\":\"Runtime.enable\"}"),
                TaruiJsonContext.Default.WebviewDevToolsMessageOptions),
            new CommandContext("main", "main", new CapabilitySet(["plugin:webview|send-devtools-message"])));

        Assert(response.Success, "Sending a CDP message on the caller's own web view must succeed.");
        Assert(
            service.Calls.Contains("send-devtools-message|main|{\"id\":1,\"method\":\"Runtime.enable\"}"),
            "The CDP message must reach the service.");
    }

    private static async Task DeniesSendDevToolsMessageWithoutPermission()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);

        var denied = await router.InvokeAsync(
            Request(
                "plugin:webview|send-devtools-message",
                new WebviewDevToolsMessageOptions("{}"),
                TaruiJsonContext.Default.WebviewDevToolsMessageOptions),
            new CommandContext("main", "main", new CapabilitySet(["plugin:webview|list"])));

        Assert(!denied.Success, "Sending a CDP message must be denied without its permission.");
        Assert(denied.Error?.Code == "PERMISSION_DENIED", "The error must be PERMISSION_DENIED.");
        Assert(
            !service.Calls.Any(call => call.StartsWith("send-devtools-message", StringComparison.Ordinal)),
            "A denied send must never reach the service.");
    }

    private static async Task RejectsEmptyDevToolsMessage()
    {
        var service = new FakeWebviewService();
        var router = BuildRouter(service);

        var invalid = await router.InvokeAsync(
            Request(
                "plugin:webview|send-devtools-message",
                new WebviewDevToolsMessageOptions("   "),
                TaruiJsonContext.Default.WebviewDevToolsMessageOptions),
            new CommandContext("main", "main", new CapabilitySet(["plugin:webview|send-devtools-message"])));

        Assert(!invalid.Success, "An empty CDP message must be rejected.");
        Assert(invalid.Error?.Code == "INVALID_ARGUMENTS", "The error must be INVALID_ARGUMENTS.");
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
