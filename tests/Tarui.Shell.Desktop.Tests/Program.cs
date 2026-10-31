using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.WebView.Abstractions;
using Tarui.WebView.Avalonia;

namespace Tarui.Shell.Desktop.Tests;

/// <summary>
/// Desktop-only self-tests: every case materializes a real <see cref="ShellWindow"/>, so the suite needs
/// a working Avalonia window backend (Win32 / macOS). Linux runners have no X server and are skipped by
/// <c>eng/test-all.ps1</c> through <c>.requires-os.txt</c>; window-independent shell behavior lives in
/// <c>Tarui.Shell.Tests</c> and runs on every platform.
/// </summary>
internal static class Program
{
    public static async Task<int> Main()
    {
        EnsureAvaloniaInitialized();

        WindowExtensionContextExposesNativeWindow();
        ShellWindowMapsTransparencyOption();
        AttachWebviewRegistersInWebviewRegistry();
        await RepeatedWebviewChurnLeavesNoResidue();
        await NotifiesWebviewLifecycleObserversOnTeardown();

        Console.WriteLine("Tarui.Shell desktop self-tests passed.");
        return 0;
    }

    private static void EnsureAvaloniaInitialized()
    {
        // Constructing a native ShellWindow requires the Avalonia windowing platform. Wire the concrete
        // desktop backend up without entering the message loop.
        AppBuilder.Configure<Avalonia.Application>()
            .UsePlatformDetect()
            .SetupWithoutStarting();
    }

    private static void WindowExtensionContextExposesNativeWindow()
    {
        // The context surfaces the live native window so an extension can manipulate it directly.
        // The composition is assigned its owning window only once a window is built.
        var window = new ShellWindow(new WindowOptions("main"));
        var context = new WindowExtensionContext(
            "main",
            new CommandContext("main", "main", new CapabilitySet([])),
            window.Composition,
            EmptyProvider.Instance,
            new EventRouter(new WindowRegistry(), new WebviewRegistry(), new EventHub()));

        Assert(
            ReferenceEquals(context.Window, window),
            "The window extension context must surface the live native window.");
        Assert(
            ReferenceEquals(context.Window, window.Composition.Window),
            "The surfaced window must be the one attached to the composition.");
    }

    private static void ShellWindowMapsTransparencyOption()
    {
        var transparent = new ShellWindow(new WindowOptions("main") { Transparent = true });
        var opaque = new ShellWindow(new WindowOptions("main"));

        Assert(
            transparent.TransparencyLevelHint.Contains(WindowTransparencyLevel.AcrylicBlur) &&
            transparent.TransparencyLevelHint.Contains(WindowTransparencyLevel.Transparent),
            "A transparent window must hint acrylic blur with plain transparency as fallback.");
        Assert(
            transparent.Background == Avalonia.Media.Brushes.Transparent &&
            transparent.TransparencyBackgroundFallback == Avalonia.Media.Brushes.Transparent,
            "A transparent window must keep every backdrop brush transparent.");
        Assert(
            !opaque.TransparencyLevelHint.Contains(WindowTransparencyLevel.Transparent),
            "A window without the transparent option must keep the default transparency hint.");
    }

    private static void AttachWebviewRegistersInWebviewRegistry()
    {
        var (provider, attacher, webviews, _, shell) = CreateAttacherHarness();
        using (provider)
        {
            var webview = attacher.AttachWebview(
                shell,
                "main",
                "side",
                new Uri("http://127.0.0.1:5173/"),
                new WebviewBounds(10, 20, 100, 50));

            Assert(webviews.TryGet("side", out var stored), "An attached web view must be registered.");
            Assert(
                stored.WebviewLabel == "side" && stored.WindowLabel == "main",
                "The entry must carry both the web view and host window labels.");
            Assert(webviews.WebviewLabelsFor("main").Contains("side"), "The window index must include the attached surface.");
            Assert(shell.Composition.Content.Children.Contains(webview.Presenter), "The surface must be mounted on the window.");
            Assert(webview.Presenter.SurfaceBounds is not null, "A bounded surface must keep its bounds.");
        }
    }

    private static async Task RepeatedWebviewChurnLeavesNoResidue()
    {
        var (provider, attacher, webviews, _, shell) = CreateAttacherHarness();
        using (provider)
        {
            for (var index = 0; index < 100; index++)
            {
                var label = $"dyn-{index}";
                attacher.AttachWebview(shell, "main", label, new Uri("http://127.0.0.1:5173/"), new WebviewBounds(0, 0, 10, 10));
                await attacher.DestroyWebviewAsync(label);
            }

            Assert(webviews.WebviewLabelsFor("main").Count == 0, "No dynamic web view may remain registered after churn.");
            Assert(shell.Composition.Content.Children.Count == 0, "No surface may remain mounted after churn.");
            await attacher.DestroyWebviewAsync("dyn-0");
            Assert(webviews.WebviewLabels.Count == 0, "Destroying an already-removed web view must stay a no-op.");
        }
    }

    private static async Task NotifiesWebviewLifecycleObserversOnTeardown()
    {
        var observer = new RecordingWebviewLifecycleObserver();
        var (provider, attacher, _, _, shell) = CreateAttacherHarness(observer);
        using (provider)
        {
            attacher.AttachWebview(shell, "main", "dyn", new Uri("http://127.0.0.1:5173/"), new WebviewBounds(0, 0, 10, 10));
            await attacher.DestroyWebviewAsync("dyn");
            Assert(observer.Closed.Contains("main|dyn"),
                "Destroying a web view must notify lifecycle observers with both labels.");
            await attacher.DestroyWebviewAsync("dyn");
            Assert(observer.Closed.Count == 1, "A repeated destroy must not notify observers twice.");
        }
    }

    private static (ServiceProvider Provider, WebviewAttacher Attacher, WebviewRegistry Webviews, WindowRegistry Registry, ShellWindow Shell)
        CreateAttacherHarness(IWebviewLifecycleObserver? lifecycleObserver = null)
    {
        // Mounting surfaces touches a real ShellWindow, so the Avalonia windowing platform must be up.
        var services = new ServiceCollection();
        services.AddTaruiShell();
        services.AddSingleton(new TaruiAppOrigin(new Uri("http://127.0.0.1:5173/")));
        services.AddSingleton(new WindowOptions("main"));
        services.AddSingleton<ITaruiAvaloniaWebViewFactory>(new NewWebViewFactory());
        if (lifecycleObserver is not null)
        {
            services.AddSingleton(lifecycleObserver);
        }

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<WindowRegistry>();
        var shell = ShellWindowFactory.Create(new WindowOptions("main"));
        registry.Add("main", new WindowRegistry.Entry(
            shell,
            new FakeSink(),
            new CommandContext("main", "main", new CapabilitySet([]))));

        return (
            provider,
            provider.GetRequiredService<WebviewAttacher>(),
            provider.GetRequiredService<WebviewRegistry>(),
            registry,
            shell);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public static EmptyProvider Instance { get; } = new();

        private EmptyProvider()
        {
        }

        public object? GetService(Type serviceType) => null;
    }

    /// <summary>Creates a fresh <see cref="FakeWebView"/> per call, so each mounted surface owns its control.</summary>
    private sealed class NewWebViewFactory : ITaruiAvaloniaWebViewFactory
    {
        public ITaruiAvaloniaWebView Create(TaruiWebViewOptions options) => new FakeWebView();
    }

    private sealed class RecordingWebviewLifecycleObserver : IWebviewLifecycleObserver
    {
        public List<string> Closed { get; } = [];

        public void OnWebviewClosed(string windowLabel, string webviewLabel) =>
            Closed.Add(windowLabel + "|" + webviewLabel);
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

    /// <summary>
    /// Minimal Avalonia-backed web view: the desktop suite only mounts and tears down surfaces, so the
    /// browser events stay unraised while the control host is real.
    /// </summary>
    private sealed class FakeWebView : ITaruiAvaloniaWebView, IAsyncDisposable
    {
#pragma warning disable CS0067 // The browser events are part of the interface but unused by the mount/teardown tests.
        public event EventHandler<TaruiWebMessage>? MessageReceived;
        public event EventHandler<TaruiWebViewFileDropEventArgs>? FileDropEntered;
        public event EventHandler<TaruiWebViewFileDropLeftEventArgs>? FileDropLeft;
        public event EventHandler<TaruiWebViewFileDropEventArgs>? FileDropped;
        public event EventHandler<TaruiWebViewDownloadEventArgs>? DownloadRequested;
        public event EventHandler<TaruiWebViewNavigationEventArgs>? NavigationRequested;
        public event EventHandler<TaruiWebViewDragRegionEventArgs>? DragRegionsUpdated;
        public event EventHandler<TaruiWebViewRenderProcessGoneEventArgs>? RenderProcessGone;
#pragma warning restore CS0067

        public Control Control { get; } = new Border();

        public Uri? Source { get; private set; }

        public void Navigate(Uri source) => Source = source;

        public void SetDevTools(bool open)
        {
        }

        public ValueTask ExecuteScriptAsync(string script, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public IReadOnlyList<DraggableRegion> SetDragRegions(IReadOnlyList<DraggableRegion> regions) => [];

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
