using System.Text.Json;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.Events;
using Tarui.Plugins.System;
using Tarui.Plugins.Window;
using Tarui.WebView.Abstractions;
using Tarui.WebView.Avalonia;

namespace Tarui.Shell.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        await RegistryTracksWindowEntries();
        EntryCarriesWebviewSessionAsSink();
        await RouterDeliversEventsToTargetWindows();
        await RouterBroadcastsToAllWindows();
        await RouterNotifiesHubSubscribers();
        await RouterRoutesByTargetWindowPresence();
        await WebViewHostRejectsFileDropWithoutCapability();
        await WebViewHostDeliversFileDropToAuthorizedWindow();
        await WebViewHostGatesDownloadByPolicyAndCapability();
        await WebViewHostGatesNavigationByPolicyAndCapability();
        await WebViewHostDeniesUnsafeSchemeWithoutThrowing();
        await WebViewHostDeniesMalformedDownloadUrlWithoutThrowing();
        await WebViewHostDisposeIsIdempotent();
        await WebViewHostDisposeAsyncIsIdempotent();
        await CapabilityLoaderMergesWindowPermissions();
        await CapabilityLoaderHandlesMissingDirectory();
        ComposerRegistersPluginCommands();
        ComposerRejectsUnregisteredPermissions();
        CapabilitySetProviderCachesDirectorySnapshot();
        AddTaruiShellRegistersShellServices();
        ShellPolicyAcceptsAllApplicationSchemes();
        Console.WriteLine("Tarui.Shell self-tests passed.");
        return 0;
    }

    private static async Task RegistryTracksWindowEntries()
    {
        var registry = new WindowRegistry();
        var mainSink = new FakeSink();
        var editorSink = new FakeSink();
        var context = new CommandContext("main", "main", new CapabilitySet([]));

        registry.Add("main", CreateEntry(mainSink, context));
        registry.Add("editor", CreateEntry(editorSink, context));
        Assert(
            registry.Labels.OrderBy(static label => label, StringComparer.Ordinal).SequenceEqual(["editor", "main"]),
            "The registry must expose all window labels.");

        Assert(registry.Get("main").Sink == mainSink, "Get must return the stored entry.");
        Assert(registry.TryGet("editor", out var editor) && editor.Sink == editorSink, "TryGet must find windows.");
        Assert(!registry.TryGet("missing", out _), "TryGet must not find unknown windows.");

        Assert(
            registry.TryGetSink("editor", out var sink) && sink == editorSink,
            "TryGetSink must resolve the window sink.");

        var duplicate = false;
        try
        {
            registry.Add("main", CreateEntry(mainSink, context));
        }
        catch (InvalidOperationException)
        {
            duplicate = true;
        }

        Assert(duplicate, "Adding a duplicate label must fail.");

        Assert(registry.Remove("editor"), "Remove must delete the window.");
        Assert(!registry.TryGet("editor", out _), "Removed windows must be gone.");

        var missing = false;
        try
        {
            _ = registry.Get("editor");
        }
        catch (KeyNotFoundException)
        {
            missing = true;
        }

        Assert(missing, "Getting an unknown label must fail.");
    }

    private static async Task RouterDeliversEventsToTargetWindows()
    {
        var registry = new FakeSinkRegistry();
        var main = registry.Add("main");
        var editor = registry.Add("editor");
        var router = new EventRouter(registry, new EventHub());
        var payload = JsonSerializer.SerializeToElement(new WindowFocusChanged(true), TaruiJsonContext.Default.WindowFocusChanged);

        await router.EmitToWindowAsync("editor", "window://focus-changed", payload);

        Assert(main.Events.Count == 0, "A targeted emit must not reach other windows.");
        Assert(
            editor.Events.Count == 1 && editor.Events[0].Event == "window://focus-changed",
            "A targeted emit must reach the target window.");
        Assert(
            editor.Events[0].Payload.GetProperty("focused").GetBoolean(),
            "The event payload must round-trip.");
    }

    private static async Task RouterBroadcastsToAllWindows()
    {
        var registry = new FakeSinkRegistry();
        var main = registry.Add("main");
        var editor = registry.Add("editor");
        var router = new EventRouter(registry, new EventHub());
        var payload = JsonSerializer.SerializeToElement(
            new WindowLabelOptions("editor"),
            TaruiJsonContext.Default.WindowLabelOptions);

        await router.EmitToAllAsync("window://destroyed", payload);

        Assert(main.Events.Count == 1, "A broadcast must reach every window.");
        Assert(editor.Events.Count == 1, "A broadcast must reach every window.");
        Assert(
            main.Events[0].Event == "window://destroyed" && editor.Events[0].Event == "window://destroyed",
            "The broadcast event name must round-trip.");
    }

    private static async Task RouterNotifiesHubSubscribers()
    {
        var registry = new FakeSinkRegistry();
        var router = new EventRouter(registry, new EventHub());
        var received = new List<string>();
        using var subscription = router.Subscribe<JsonElement>(
            "shell://theme-changed",
            payload => received.Add(payload.GetProperty("theme").GetString() ?? string.Empty));

        var payload = JsonSerializer.SerializeToElement(new ThemeChanged("dark"), TaruiJsonContext.Default.ThemeChanged);
        await router.EmitToWindowAsync("main", "shell://theme-changed", payload);

        Assert(received.SequenceEqual(["dark"]), "Hub subscribers must observe emitted events.");

        received.Clear();
        subscription.Dispose();
        await router.EmitToWindowAsync("main", "shell://theme-changed", payload);
        Assert(received.Count == 0, "Disposed subscriptions must stop receiving events.");
    }

    private static async Task RouterRoutesByTargetWindowPresence()
    {
        var registry = new FakeSinkRegistry();
        var main = registry.Add("main");
        var editor = registry.Add("editor");
        var router = new EventRouter(registry, new EventHub());
        var payload = JsonSerializer.SerializeToElement(new Unit(), TaruiJsonContext.Default.Unit);

        await router.EmitAsync("a://event", payload, "editor");
        Assert(
            editor.Events.Count == 1 && main.Events.Count == 0,
            "A named target must deliver to that window only.");

        await router.EmitAsync("a://event", payload, null);
        Assert(
            editor.Events.Count == 2 && main.Events.Count == 1,
            "A null target must broadcast to all windows.");

        await router.EmitAsync("a://event", payload, "missing");
        Assert(
            editor.Events.Count == 2 && main.Events.Count == 1,
            "An unknown target must be a no-op for sinks.");
    }

    private static async Task CapabilityLoaderMergesWindowPermissions()
    {
        using var directory = new TempDirectory();
        directory.Write(
            "main.json",
            """
            {
              "identifier": "main",
              "windows": ["main"],
              "permissions": ["core:app|get-info"]
            }
            """);
        directory.Write(
            "shared.json",
            """
            {
              "identifier": "shared",
              "windows": ["main", "editor"],
              "permissions": ["core:window|list"]
            }
            """);
        directory.Write(
            "editor.json",
            """
            {
              "identifier": "editor",
              "windows": ["editor"],
              "permissions": ["plugin:dialog|open"]
            }
            """);

        var capabilities = CapabilityLoader.Load(directory.Path);
        Assert(capabilities.Count == 2, "Only windows referenced by capability files must appear.");

        var main = capabilities["main"];
        Assert(main.Allows("core:app|get-info"), "The main window must inherit its own permissions.");
        Assert(main.Allows("core:window|list"), "The main window must inherit shared permissions.");
        Assert(!main.Allows("plugin:dialog|open"), "The main window must not gain editor permissions.");

        var editor = capabilities["editor"];
        Assert(editor.Allows("core:window|list"), "The editor window must inherit shared permissions.");
        Assert(editor.Allows("plugin:dialog|open"), "The editor window must inherit its own permissions.");
        Assert(!editor.Allows("core:app|get-info"), "The editor window must not gain main permissions.");
    }

    private static async Task CapabilityLoaderHandlesMissingDirectory()
    {
        var capabilities = CapabilityLoader.Load(Path.Combine(Path.GetTempPath(), $"tarui-missing-{Guid.NewGuid():N}"));
        Assert(capabilities.Count == 0, "A missing capability directory must yield no capabilities.");
    }

    private static void ComposerRegistersPluginCommands()
    {
        using var directory = new TempDirectory();
        directory.Write(
            "main.json",
            """
            {
              "identifier": "main",
              "windows": ["main"],
              "permissions": ["test:shell|one", "test:shell|two"]
            }
            """);

        var services = new ServiceCollection();
        services.AddPlugin<TestShellPlugin>();
        services.AddSingleton<ICapabilityProvider>(new CapabilitySetProvider(directory.Path));
        using var provider = services.BuildServiceProvider();

        var router = CommandRouterComposer.Compose(provider);

        Assert(
            router.Commands.Contains("test:shell|one") && router.Commands.Contains("test:shell|two"),
            "Compose must route every plugin command.");
        Assert(
            router.RegisteredPermissions.Contains("test:shell|one") &&
            router.RegisteredPermissions.Contains("test:shell|two"),
            "Compose must expose every plugin permission.");
    }

    private static void ComposerRejectsUnregisteredPermissions()
    {
        using var directory = new TempDirectory();
        directory.Write(
            "main.json",
            """
            {
              "identifier": "main",
              "windows": ["main"],
              "permissions": ["test:shell|one", "test:shell|missing"]
            }
            """);

        var services = new ServiceCollection();
        services.AddPlugin<TestShellPlugin>();
        services.AddSingleton<ICapabilityProvider>(new CapabilitySetProvider(directory.Path));
        using var provider = services.BuildServiceProvider();

        var rejected = false;
        try
        {
            CommandRouterComposer.Compose(provider);
        }
        catch (InvalidOperationException exception)
        {
            rejected = exception.Message.Contains("test:shell|missing");
        }

        Assert(rejected, "Compose must reject capability files that reference unregistered permissions.");
    }

    private static void CapabilitySetProviderCachesDirectorySnapshot()
    {
        using var directory = new TempDirectory();
        directory.Write(
            "main.json",
            """
            {
              "identifier": "main",
              "windows": ["main"],
              "permissions": ["test:shell|one"]
            }
            """);
        var provider = new CapabilitySetProvider(directory.Path);

        var first = provider.Capabilities;
        Assert(first.ContainsKey("main"), "The provider must read the configured directory.");
        Assert(
            first["main"].Allows("test:shell|one"),
            "The provider must expose the granted permission.");

        directory.Write(
            "extra.json",
            """
            {
              "identifier": "extra",
              "windows": ["main"],
              "permissions": ["test:shell|two"]
            }
            """);

        var second = provider.Capabilities;
        Assert(ReferenceEquals(first, second), "Repeated access must return the cached snapshot.");
        Assert(!second["main"].Allows("test:shell|two"), "The capability directory must only be read once.");
    }

    private static void AddTaruiShellRegistersShellServices()
    {
        var services = new ServiceCollection();
        services.AddTaruiShell();
        services.AddSingleton(new TaruiAppOrigin(new Uri("http://127.0.0.1:5173/")));
        services.AddSingleton(new WindowOptions("main"));

        using var provider = services.BuildServiceProvider();

        Assert(
            provider.GetRequiredService<IMainWindowLauncher>() is MainWindowLauncher,
            "AddTaruiShell must register the main window launcher.");
        Assert(
            provider.GetRequiredService<IWindowService>() is AvaloniaWindowService,
            "AddTaruiShell must register the window service.");
        Assert(
            provider.GetRequiredService<IEventSender>() is not null,
            "AddTaruiShell must register the event sender.");
        Assert(
            provider.GetRequiredService<IDialogService>() is not null,
            "AddTaruiShell must register the dialog service.");
        Assert(
            provider.GetRequiredService<IClipboardService>() is not null,
            "AddTaruiShell must register the clipboard service.");
        Assert(
            provider.GetRequiredService<IpcDispatcher>() is not null,
            "AddTaruiShell must compose an empty plugin set without failing.");
        Assert(
            provider.GetRequiredService<CommandRouter>() is not null,
            "AddTaruiShell must register the command router.");
    }

    private static void ShellPolicyAcceptsAllApplicationSchemes()
    {
        // The default policy must cover every application origin: the HTTP start origin, the portless
        // custom app scheme served from local assets, and local dev servers. Everything else stays
        // denied unless it is an https target, which is handed to the OS handler.
        var services = new ServiceCollection();
        services.AddTaruiShell();
        services.AddSingleton(new TaruiAppOrigin(
            new Uri("http://127.0.0.1:5173/"),
            ["http", "https", "tarui"],
            new Uri("tarui://localhost/")));
        services.AddSingleton(new WindowOptions("main"));

        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<WebViewRequestPolicy>();

        Assert(
            policy.DecideNavigation(new Uri("http://127.0.0.1:5173/app")) == WebViewRequestDecision.Allow,
            "The HTTP start origin must be allowed.");
        Assert(
            policy.DecideNavigation(new Uri("http://localhost:3000/page")) == WebViewRequestDecision.Allow,
            "Local dev servers must stay allowed.");
        Assert(
            policy.DecideNavigation(new Uri("tarui://localhost/index.html")) == WebViewRequestDecision.Allow,
            "The portless custom scheme origin must be allowed.");
        Assert(
            policy.DecideNavigation(new Uri("tarui://localhost/assets/app.js")) == WebViewRequestDecision.Allow,
            "Assets under the custom scheme origin must be allowed.");
        Assert(
            policy.DecideNavigation(new Uri("tarui://localhost:8080/index.html")) == WebViewRequestDecision.Deny,
            "A ported custom scheme URL must not slip through the portless origin pattern.");
        Assert(
            policy.DecideNavigation(new Uri("tarui://other/index.html")) == WebViewRequestDecision.Deny,
            "Unlisted custom scheme hosts must be denied.");
        Assert(
            policy.DecideNavigation(new Uri("https://example.com/docs")) == WebViewRequestDecision.External,
            "Unlisted https targets must be handed to the OS handler.");
        Assert(
            policy.DecideNavigation(new Uri("http://example.com/")) == WebViewRequestDecision.Deny,
            "Unlisted http targets must be denied.");
    }

    private static void EntryCarriesWebviewSessionAsSink()
    {
        // The window entry is decoupled from any particular presentation: its event sink is the
        // UI-neutral web view session it references, so window and webview stay addressable separately.
        var context = new CommandContext("main", "main", new CapabilitySet([], [], []));
        var (session, _, _) = CreateWebViewHost(new CapabilitySet([], [], []));

        var entry = new WindowRegistry.Entry(null!, session, context);

        Assert(ReferenceEquals(entry.Sink, session), "The entry sink must be its web view session.");
        Assert(entry.Context == context, "The entry must retain its command context.");
    }

    private static Task WebViewHostRejectsFileDropWithoutCapability()
    {
        var (host, sink, webView) = CreateWebViewHost(new CapabilitySet([], [], []));

        using (host)
        {
            var args = webView.RaiseFileDropEntered([@"C:\drop\file.txt"], null, 10, 20);

            Assert(!args.Accepted, "A drop to an un-authorized window must be rejected at the OS layer.");
            Assert(sink.Events.Count == 0, "An un-authorized window must never receive file-drop paths.");
        }

        return Task.CompletedTask;
    }

    private static Task WebViewHostDeliversFileDropToAuthorizedWindow()
    {
        var capabilities = new CapabilitySet(
            [],
            ["window://file-drop-entered", "window://file-dropped", "window://file-drop-left"],
            []);
        var (host, sink, webView) = CreateWebViewHost(capabilities);

        var text = "plain text payload";
        using (host)
        {
            var entered = webView.RaiseFileDropEntered([@"C:\a.txt", @"C:\b.txt"], text, 5, 6);
            Assert(entered.Accepted, "An authorized window must accept the file drop.");

            var dropped = webView.RaiseFileDropped([@"C:\a.txt"], text, 5, 6);
            Assert(dropped.Accepted, "An authorized window must accept the drop event.");

            webView.RaiseFileDropLeft();
        }

        Assert(sink.Events.Count == 3, "Entered, dropped and left must each be routed to an authorized window.");

        var enteredEvent = sink.Events.Single(e => e.Event == "window://file-drop-entered");
        Assert(
            enteredEvent.Payload.GetProperty("paths").GetArrayLength() == 2,
            "The entered payload must carry the dragged file paths.");
        Assert(
            enteredEvent.Payload.GetProperty("text").GetString() == text,
            "The entered payload must carry the dropped text.");
        Assert(
            Math.Abs(enteredEvent.Payload.GetProperty("x").GetDouble() - 5) < 1e-9 &&
            Math.Abs(enteredEvent.Payload.GetProperty("y").GetDouble() - 6) < 1e-9,
            "The entered payload must carry the drop position.");

        Assert(
            sink.Events.Any(e => e.Event == "window://file-dropped") &&
            sink.Events.Any(e => e.Event == "window://file-drop-left"),
            "Dropped and left must both be delivered.");

        return Task.CompletedTask;
    }

    private static Task WebViewHostGatesDownloadByPolicyAndCapability()
    {
        // Policy allows the host; capability authorizes the event => delivered and allowed.
        var allowed = new CapabilitySet([], ["webview://download-requested"], []);
        var (allowedHost, allowedSink, allowedWebView) = CreateWebViewHost(
            allowed,
            new WebViewRequestPolicy(new WebViewPolicyOptions([], [], ["cdn.example"], WebViewRequestDecision.Deny)));

        using (allowedHost)
        {
            var args = allowedWebView.RaiseDownload("https://cdn.example/report.pdf", "report.pdf");

            Assert(
                args.Decision == TaruiWebViewDownloadAction.Allow,
                "A download from a policy-allowed host must be allowed.");
        }

        Assert(
            allowedSink.Events.Any(e => e.Event == "webview://download-requested"),
            "An authorized, allowed download must be delivered to the window.");

        // Policy denies the host => denied and not delivered even with the event capability.
        var deniedByPolicy = new CapabilitySet([], ["webview://download-requested"], []);
        var (deniedHost, deniedSink, deniedWebView) = CreateWebViewHost(
            deniedByPolicy,
            new WebViewRequestPolicy(new WebViewPolicyOptions([], [], [], WebViewRequestDecision.Deny)));

        using (deniedHost)
        {
            var args = deniedWebView.RaiseDownload("https://evil.example/trojan.exe", null);
            Assert(args.Decision == TaruiWebViewDownloadAction.Deny,
                "A download from an unlisted host must be denied.");
        }

        Assert(deniedSink.Events.Count == 0, "A denied download must not be delivered.");

        // Policy allows the host but the window lacks the event capability => denied and not delivered.
        var lackEventCapability = new CapabilitySet([], [], []);
        var (silentHost, silentSink, silentWebView) = CreateWebViewHost(
            lackEventCapability,
            new WebViewRequestPolicy(new WebViewPolicyOptions([], [], ["cdn.example"], WebViewRequestDecision.Deny)));

        using (silentHost)
        {
            var args = silentWebView.RaiseDownload("https://cdn.example/file.zip", "file.zip");
            Assert(args.Decision == TaruiWebViewDownloadAction.Deny,
                "A window without the download event capability must deny the download.");
        }

        Assert(silentSink.Events.Count == 0, "A window without the event capability must not receive the download event.");

        return Task.CompletedTask;
    }

    private static Task WebViewHostDeniesMalformedDownloadUrlWithoutThrowing()
    {
        var capabilities = new CapabilitySet([], ["webview://download-requested"], []);
        var (host, sink, webView) = CreateWebViewHost(
            capabilities,
            new WebViewRequestPolicy(new WebViewPolicyOptions([], [], [], WebViewRequestDecision.Allow)));

        using (host)
        {
            var args = webView.RaiseDownload("not a valid absolute uri", "payload.bin");
            Assert(
                args.Decision == TaruiWebViewDownloadAction.Deny,
                "A malformed download URL must be denied without throwing.");
        }

        Assert(sink.Events.Count == 0, "A malformed download URL must not be delivered to the window.");
        return Task.CompletedTask;
    }

    private static Task WebViewHostDisposeIsIdempotent()
    {
        var (host, _, webView) = CreateWebViewHost(new CapabilitySet([], [], []));

        host.Dispose();
        host.Dispose();
        webView.RaiseFileDropLeft();

        Assert(webView.DisposeCallCount == 1, "Synchronous WebViewHost disposal must call Dispose exactly once.");
        Assert(webView.DisposeAsyncCallCount == 0, "Synchronous WebViewHost disposal must not call DisposeAsync.");
        return Task.CompletedTask;
    }

    private static async Task WebViewHostDisposeAsyncIsIdempotent()
    {
        var (host, _, webView) = CreateWebViewHost(new CapabilitySet([], [], []));

        await host.DisposeAsync();
        await host.DisposeAsync();
        webView.RaiseFileDropLeft();

        Assert(webView.DisposeCallCount == 0, "Asynchronous WebViewHost disposal must not call Dispose.");
        Assert(webView.DisposeAsyncCallCount == 1, "Asynchronous WebViewHost disposal must call DisposeAsync exactly once.");
    }

    private static Task WebViewHostGatesNavigationByPolicyAndCapability()
    {
        // Policy allows and capability authorizes => allowed and delivered.
        var allowed = new CapabilitySet([], ["webview://navigation-requested"], []);
        var (allowedHost, allowedSink, allowedWebView) = CreateWebViewHost(
            allowed,
            new WebViewRequestPolicy(new WebViewPolicyOptions(
                ["https://app.example/**"], [], [], WebViewRequestDecision.Deny)));

        using (allowedHost)
        {
            var args = allowedWebView.RaiseNavigation(new Uri("https://app.example/home"), isMainFrame: true);

            Assert(
                args.Decision == TaruiWebViewNavigationAction.Allow,
                "An allowed navigation must be permitted inside the web view.");
        }

        Assert(
            allowedSink.Events.Any(e => e.Event == "webview://navigation-requested"),
            "An allowed, authorized navigation must be delivered.");
        var navPayload = allowedSink.Events.Single(e => e.Event == "webview://navigation-requested").Payload;
        Assert(navPayload.GetProperty("isMainFrame").GetBoolean(),
            "The navigation payload must reflect the main-frame flag.");
        Assert(navPayload.GetProperty("url").GetString() == "https://app.example/home",
            "The navigation payload must carry the resolved URL.");

        // Policy denies => navigation blocked, event withheld.
        var denied = new CapabilitySet([], ["webview://navigation-requested"], []);
        var (deniedHost, deniedSink, deniedWebView) = CreateWebViewHost(
            denied,
            new WebViewRequestPolicy(new WebViewPolicyOptions([], [], [], WebViewRequestDecision.Deny)));

        using (deniedHost)
        {
            var args = deniedWebView.RaiseNavigation(new Uri("https://evil.example/steal"), isMainFrame: true);
            Assert(args.Decision == TaruiWebViewNavigationAction.Deny,
                "An unmatched navigation must be denied.");
        }

        Assert(deniedSink.Events.Count == 0, "A denied navigation must not be delivered.");

        return Task.CompletedTask;
    }

    private static Task WebViewHostDeniesUnsafeSchemeWithoutThrowing()
    {
        // CEF loads about:blank before the real start URL; an unsafe-scheme request must be
        // cancelled as a plain deny instead of escaping the policy as an exception.
        var capabilities = new CapabilitySet([], ["webview://navigation-requested", "webview://download-requested"], []);
        var (host, sink, webView) = CreateWebViewHost(
            capabilities,
            new WebViewRequestPolicy(new WebViewPolicyOptions([], [], [], WebViewRequestDecision.Deny)));

        using (host)
        {
            var navigation = webView.RaiseNavigation(new Uri("about:blank"), isMainFrame: true);
            Assert(
                navigation.Decision == TaruiWebViewNavigationAction.Deny,
                "The initial about:blank navigation must be denied, not throw.");

            var script = webView.RaiseNavigation(new Uri("javascript:alert(1)"), isMainFrame: true);
            Assert(
                script.Decision == TaruiWebViewNavigationAction.Deny,
                "A javascript: navigation must be denied without throwing.");

            var download = webView.RaiseDownload("javascript:alert(1)", null);
            Assert(
                download.Decision == TaruiWebViewDownloadAction.Deny,
                "An unsafe-scheme download must be denied without throwing.");
        }

        Assert(sink.Events.Count == 0, "Unsafe-scheme denials must not be delivered to the window.");

        return Task.CompletedTask;
    }

    private static (WebviewSession Host, FakeSink Sink, FakeWebView WebView) CreateWebViewHost(
        CapabilitySet capabilities,
        WebViewRequestPolicy? policy = null)
    {
        var registry = new FakeSinkRegistry();
        var sink = registry.Add("main", capabilities);
        var router = new EventRouter(registry, new EventHub());
        var dispatcher = new IpcDispatcher(new CommandRouterBuilder().Build());
        var webView = new FakeWebView();

        var host = new WebviewSession(
            new FixedWebViewFactory(webView),
            dispatcher,
            router,
            policy ?? new WebViewRequestPolicy(new WebViewPolicyOptions(
                ["http://localhost:*/*"],
                ["https:*"],
                [],
                WebViewRequestDecision.Deny)),
            new CommandContext("main", "main", capabilities),
            new Uri("http://127.0.0.1:5173/"));

        return (host, sink, webView);
    }

    private sealed class FixedWebViewFactory(ITaruiAvaloniaWebView webView) : ITaruiAvaloniaWebViewFactory
    {
        public ITaruiAvaloniaWebView Create(TaruiWebViewOptions options) => webView;
    }

    private sealed class FakeWebView : ITaruiAvaloniaWebView, IAsyncDisposable
    {
#pragma warning disable CS0067 // The message and drag-region events are part of the interface but unused by routing tests.
        public event EventHandler<TaruiWebMessage>? MessageReceived;
        public event EventHandler<TaruiWebViewFileDropEventArgs>? FileDropEntered;
        public event EventHandler<TaruiWebViewFileDropLeftEventArgs>? FileDropLeft;
        public event EventHandler<TaruiWebViewFileDropEventArgs>? FileDropped;
        public event EventHandler<TaruiWebViewDownloadEventArgs>? DownloadRequested;
        public event EventHandler<TaruiWebViewNavigationEventArgs>? NavigationRequested;
        public event EventHandler<TaruiWebViewDragRegionEventArgs>? DragRegionsUpdated;
#pragma warning restore CS0067

        public Control Control { get; } = new Border();

        public Uri? Source { get; private set; }

        public int DisposeCallCount { get; private set; }

        public int DisposeAsyncCallCount { get; private set; }

        public void Navigate(Uri source)
        {
            Source = source;
        }

        public ValueTask<string?> ExecuteScriptAsync(
            string script,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);

        public IReadOnlyList<DraggableRegion> SetDragRegions(IReadOnlyList<DraggableRegion> regions) => [];

        public void Dispose()
        {
            DisposeCallCount++;
        }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCallCount++;
            return ValueTask.CompletedTask;
        }

        public TaruiWebViewFileDropEventArgs RaiseFileDropEntered(
            string[] paths, string? text, double x, double y)
        {
            var args = new TaruiWebViewFileDropEventArgs(paths, text, x, y);
            FileDropEntered?.Invoke(this, args);
            return args;
        }

        public TaruiWebViewFileDropEventArgs RaiseFileDropped(
            string[] paths, string? text, double x, double y)
        {
            var args = new TaruiWebViewFileDropEventArgs(paths, text, x, y);
            FileDropped?.Invoke(this, args);
            return args;
        }

        public void RaiseFileDropLeft() =>
            FileDropLeft?.Invoke(this, TaruiWebViewFileDropLeftEventArgs.Instance);

        public TaruiWebViewDownloadEventArgs RaiseDownload(string url, string? suggestedFilename)
        {
            var args = new TaruiWebViewDownloadEventArgs(url, suggestedFilename);
            DownloadRequested?.Invoke(this, args);
            return args;
        }

        public TaruiWebViewNavigationEventArgs RaiseNavigation(Uri url, bool isMainFrame)
        {
            var args = new TaruiWebViewNavigationEventArgs(url, isMainFrame);
            NavigationRequested?.Invoke(this, args);
            return args;
        }
    }

    private sealed class TestShellPlugin : ITaruiPlugin
    {
        public void ConfigureCommands(CommandRouterBuilder commands)
        {
            commands.Add(
                "test:shell|one",
                TaruiJsonContext.Default.EmptyArgs,
                TaruiJsonContext.Default.Unit,
                static (_, _, _) => ValueTask.FromResult(new Unit()),
                "test:shell|one");
            commands.Add(
                "test:shell|two",
                TaruiJsonContext.Default.EmptyArgs,
                TaruiJsonContext.Default.Unit,
                static (_, _, _) => ValueTask.FromResult(new Unit()),
                "test:shell|two");
        }
    }

    private static WindowRegistry.Entry CreateEntry(FakeSink sink, CommandContext context) =>
        new(null!, sink, context);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeSink : IEventSink
    {
        public List<(string Event, JsonElement Payload)> Events { get; } = [];

        public ValueTask SendEventAsync(
            string eventName,
            JsonElement payload,
            CancellationToken cancellationToken = default)
        {
            Events.Add((eventName, payload));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSinkRegistry : IWindowSinkRegistry
    {
        private readonly Dictionary<string, FakeSink> _sinks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CapabilitySet> _capabilities = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Labels => _sinks.Keys.ToArray();

        public FakeSink Add(string label) =>
            Add(label, new CapabilitySet(["*"], ["*"], []));

        public FakeSink Add(string label, CapabilitySet capabilities)
        {
            var sink = new FakeSink();
            _sinks[label] = sink;
            _capabilities[label] = capabilities;
            return sink;
        }

        public bool TryGetSink(string label, out IEventSink sink)
        {
            if (_sinks.TryGetValue(label, out var fake))
            {
                sink = fake;
                return true;
            }

            sink = null!;
            return false;
        }

        public bool TryGetCapabilities(string label, out CapabilitySet capabilities)
        {
            if (_capabilities.TryGetValue(label, out var set))
            {
                capabilities = set;
                return true;
            }

            capabilities = null!;
            return false;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() => Path = Directory.CreateTempSubdirectory("tarui-shell-tests-").FullName;

        public string Path { get; }

        public void Write(string fileName, string content) =>
            File.WriteAllText(System.IO.Path.Combine(Path, fileName), content);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup for temporary test data.
            }
        }
    }
}
