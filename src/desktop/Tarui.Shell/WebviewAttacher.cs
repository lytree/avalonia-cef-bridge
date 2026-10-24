using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.WebView.Abstractions;
using Tarui.WebView.Avalonia;

namespace Tarui.Shell;

/// <summary>
/// Assembles a window end-to-end: resolves the window capability, creates a UI-neutral
/// <see cref="WebviewSession"/>, wraps it in a visual <see cref="WebviewPresenter"/>, mounts that onto a
/// freshly created <see cref="ShellWindow"/> and wires the native window lifecycle events to the router.
/// It is the composition root that connects the window shell (UI frame) with the web view surface (display),
/// so neither side has to know about the other.
/// </summary>
public sealed partial class WebviewAttacher
{
    private readonly IServiceProvider _services;
    private readonly WindowRegistry _registry;
    private readonly WebviewRegistry _webviews;
    private readonly EventRouter _eventRouter;
    private readonly WebViewRequestPolicy _requestPolicy;
    private readonly WindowCapabilityResolver _capabilityResolver;
    private readonly TaruiAppOrigin _appOrigin;
    private readonly IAppShutdownCoordinator _shutdownCoordinator;
    private readonly WindowExtensionRegistry _extensionRegistry;
    private readonly IRuntimeScopeOverlay? _scopeOverlay;
    private readonly ILogger<WebviewAttacher> _logger;

    public WebviewAttacher(
        IServiceProvider services,
        WindowRegistry registry,
        WebviewRegistry webviews,
        EventRouter eventRouter,
        WebViewRequestPolicy requestPolicy,
        WindowCapabilityResolver capabilityResolver,
        TaruiAppOrigin appOrigin,
        IAppShutdownCoordinator shutdownCoordinator,
        WindowExtensionRegistry extensionRegistry,
        IRuntimeScopeOverlay? scopeOverlay = null,
        ILogger<WebviewAttacher>? logger = null)
    {
        _services = services;
        _registry = registry;
        _webviews = webviews;
        _eventRouter = eventRouter;
        _requestPolicy = requestPolicy;
        _capabilityResolver = capabilityResolver;
        _appOrigin = appOrigin;
        _shutdownCoordinator = shutdownCoordinator;
        _extensionRegistry = extensionRegistry;
        _scopeOverlay = scopeOverlay;
        _logger = logger ?? NullLogger<WebviewAttacher>.Instance;
    }

    public WindowRegistry.Entry Attach(WindowOptions options, CommandContext? callerContext = null)
    {
        var capability = callerContext is null
            ? _capabilityResolver.Resolve(options.Label)
            : _capabilityResolver.ResolveForCreate(options.Label, callerContext);
        var context = new CommandContext(options.Label, options.Label, capability, _scopeOverlay);

        var (session, presenter) = CreateSession(context, ResolveSource(options.Url, _appOrigin));

        var window = ShellWindowFactory.Create(options);
        var extensions = ApplyExtensions(window, context, options.Label);
        window.AddWebview(presenter);

        // Register the web view as its own entity before wiring window events, so the window-level
        // fan-out sink can already resolve it when the first event fires.
        _webviews.Add(new WebviewEntry(options.Label, options.Label, session, presenter));

        var entry = new WindowRegistry.Entry(window, new WindowEventFanout(options.Label, _webviews), context);
        WireExtensionLifecycle(window, extensions);
        WireWindowEvents(window, options.Label, entry);
        return entry;
    }

    /// <summary>
    /// Mounts an additional web view inside an existing window. The new surface inherits the host
    /// window's capability profile, so attaching can never grant more authority than the window
    /// already holds. Must run on the UI thread (it touches the window's visual tree).
    /// </summary>
    public WebviewEntry AttachWebview(
        ShellWindow window,
        string windowLabel,
        string webviewLabel,
        Uri source,
        WebviewBounds? bounds)
    {
        var host = _registry.Get(windowLabel);
        var context = new CommandContext(windowLabel, webviewLabel, host.Context.Capabilities, _scopeOverlay);
        var (session, presenter) = CreateSession(context, source);

        window.AddWebview(presenter);
        if (bounds is not null)
        {
            presenter.SetBounds(bounds);
        }

        var webview = new WebviewEntry(webviewLabel, windowLabel, session, presenter);
        _webviews.Add(webview);
        return webview;
    }

    /// <summary>
    /// Unmounts and disposes a web view, releasing its surface and session. A missing label is a no-op
    /// so a repeated destroy stays idempotent. Must run on the UI thread.
    /// </summary>
    public async ValueTask DestroyWebviewAsync(string webviewLabel)
    {
        if (!_webviews.TryGet(webviewLabel, out var webview))
        {
            return;
        }

        _webviews.Remove(webviewLabel);
        if (_registry.TryGet(webview.WindowLabel, out var host) && host.Window is ShellWindow shell)
        {
            shell.RemoveWebview(webview.Presenter);
        }

        await webview.Session.DisposeAsync();
    }

    private (WebviewSession Session, WebviewPresenter Presenter) CreateSession(CommandContext context, Uri source)
    {
        // The web view factory and dispatcher resolve lazily: windows are only assembled after the
        // dispatcher is fully built, so a window can be created even if a web view backend is absent.
        var dispatcher = _services.GetRequiredService<IpcDispatcher>();
        var webViewFactory = _services.GetRequiredService<ITaruiAvaloniaWebViewFactory>();

        var session = new WebviewSession(
            webViewFactory,
            dispatcher,
            _eventRouter,
            _requestPolicy,
            context,
            source);
        return (session, new WebviewPresenter(session));
    }

    private (IShellWindowExtension Instance, WindowExtensionContext Context)[] ApplyExtensions(
        ShellWindow window,
        CommandContext context,
        string label)
    {
        var composition = window.Composition;
        var extensionContext = new WindowExtensionContext(label, context, composition, _services, _eventRouter);
        return _extensionRegistry
            .CreateFor(label, _services)
            .Select(extension =>
            {
                extension.CreateView(extensionContext);
                return (Instance: extension, Context: extensionContext);
            })
            .ToArray();
    }

    private static void WireExtensionLifecycle(
        ShellWindow window,
        (IShellWindowExtension Instance, WindowExtensionContext Context)[] extensions)
    {
        if (extensions.Length == 0)
        {
            return;
        }

        window.Opened += (_, _) =>
        {
            foreach (var (instance, context) in extensions)
            {
                instance.OnWindowLoaded(context);
            }
        };

        window.Closed += (_, _) => FireAndForget.Run(CloseExtensionsAsync(extensions));
    }

    /// <summary>
    /// Tears down a window's extensions in order: notifies each with <c>OnWindowClosed</c> and then releases it
    /// if it participates in cleanup. Split out so the lifecycle contract is unit-testable without a live window.
    /// </summary>
    internal static async ValueTask CloseExtensionsAsync(
        (IShellWindowExtension Instance, WindowExtensionContext Context)[] extensions)
    {
        foreach (var (instance, context) in extensions)
        {
            instance.OnWindowClosed(context);
            switch (instance)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    private void WireWindowEvents(Window window, string label, WindowRegistry.Entry entry)
    {
        window.PositionChanged += (_, _) => FireAndForget.Run(_eventRouter.EmitToWindowAsync(
            label,
            "window://moved",
            JsonSerializer.SerializeToElement(BuildGeometry(window), TaruiJsonContext.Default.WindowGeometry)));
        window.Resized += (_, _) => FireAndForget.Run(_eventRouter.EmitToWindowAsync(
            label,
            "window://resized",
            JsonSerializer.SerializeToElement(BuildGeometry(window), TaruiJsonContext.Default.WindowGeometry)));
        window.Activated += (_, _) => FireAndForget.Run(_eventRouter.EmitToWindowAsync(
            label,
            "window://focus-changed",
            JsonSerializer.SerializeToElement(new WindowFocusChanged(true), TaruiJsonContext.Default.WindowFocusChanged)));
        window.Deactivated += (_, _) => FireAndForget.Run(_eventRouter.EmitToWindowAsync(
            label,
            "window://focus-changed",
            JsonSerializer.SerializeToElement(new WindowFocusChanged(false), TaruiJsonContext.Default.WindowFocusChanged)));

        window.Closed += (_, _) =>
        {
            FireAndForget.Run(HandleWindowClosedAsync(label));
        };
    }

    private async ValueTask HandleWindowClosedAsync(string label)
    {
        if (!_registry.Remove(label))
        {
            return;
        }

        // A window owns its web views; closing it detaches and disposes every hosted surface.
        await DisposeWebviewsAsync(_webviews.RemoveWindow(label));

        _shutdownCoordinator.NotifyWindowClosed(label, _registry.Labels.Count);
        await _eventRouter.EmitToAllAsync(
            "window://destroyed",
            JsonSerializer.SerializeToElement(new WindowLabelOptions(label), TaruiJsonContext.Default.WindowLabelOptions));
    }

    /// <summary>
    /// Disposes every web view a window hosted. <see cref="WebviewSession"/> disposal is idempotent
    /// (an interlocked guard), so a repeated teardown never double-disposes. Split out so the teardown
    /// contract is unit-testable without a live window, mirroring <see cref="CloseExtensionsAsync"/>.
    /// </summary>
    internal static async ValueTask DisposeWebviewsAsync(IReadOnlyList<WebviewEntry> webviews)
    {
        foreach (var webview in webviews)
        {
            await webview.Session.DisposeAsync();
        }
    }

    private static WindowGeometry BuildGeometry(Window window)
    {
        var scale = window.RenderScaling;
        var position = window.Position;
        return new WindowGeometry(
            position.X / scale,
            position.Y / scale,
            window.Width,
            window.Height);
    }

    private static Uri ResolveSource(string? url, TaruiAppOrigin origin)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return origin.StartUri;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            if (!origin.AllowsScheme(absolute.Scheme))
            {
                throw new InvalidOperationException(
                    $"The URL scheme '{absolute.Scheme}' is not one of the application schemes " +
                    $"({string.Join(", ", origin.Schemes)}).");
            }

            return absolute;
        }

        return new Uri(origin.StartUri, url);
    }

    internal static void ObserveBestEffortTask(ValueTask task)
    {
        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = AwaitAndLogAsync(task);

        static async Task AwaitAndLogAsync(ValueTask valueTask)
        {
            try
            {
                await valueTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Best-effort notifications may be cancelled by the dispatcher shutting down.
            }
            catch (Exception)
            {
                // Window events are best-effort notifications; log sinks are wired separately by
                // plugins that care to observe them. Suppressing here keeps Closing/Closed handler
                // exceptions from killing the Avalonia dispatcher loop.
            }
        }
    }
}
