using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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
    private readonly EventRouter _eventRouter;
    private readonly WebViewRequestPolicy _requestPolicy;
    private readonly WindowCapabilityResolver _capabilityResolver;
    private readonly TaruiAppOrigin _appOrigin;
    private readonly IAppShutdownCoordinator _shutdownCoordinator;
    private readonly WindowExtensionRegistry _extensionRegistry;
    private readonly WindowLifecycleOptions _lifecycleOptions;
    private readonly IRuntimeScopeOverlay? _scopeOverlay;
    private readonly ILogger<WebviewAttacher> _logger;

    public WebviewAttacher(
        IServiceProvider services,
        WindowRegistry registry,
        EventRouter eventRouter,
        WebViewRequestPolicy requestPolicy,
        WindowCapabilityResolver capabilityResolver,
        TaruiAppOrigin appOrigin,
        IAppShutdownCoordinator shutdownCoordinator,
        WindowExtensionRegistry extensionRegistry,
        WindowLifecycleOptions lifecycleOptions,
        IRuntimeScopeOverlay? scopeOverlay = null,
        ILogger<WebviewAttacher>? logger = null)
    {
        _services = services;
        _registry = registry;
        _eventRouter = eventRouter;
        _requestPolicy = requestPolicy;
        _capabilityResolver = capabilityResolver;
        _appOrigin = appOrigin;
        _shutdownCoordinator = shutdownCoordinator;
        _extensionRegistry = extensionRegistry;
        _lifecycleOptions = lifecycleOptions;
        _scopeOverlay = scopeOverlay;
        _logger = logger ?? NullLogger<WebviewAttacher>.Instance;
    }

    public WindowRegistry.Entry Attach(WindowOptions options, CommandContext? callerContext = null)
    {
        var capability = callerContext is null
            ? _capabilityResolver.Resolve(options.Label)
            : _capabilityResolver.ResolveForCreate(options.Label, callerContext);
        var context = new CommandContext(options.Label, options.Label, capability, _scopeOverlay);

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
            ResolveSource(options.Url, _appOrigin));
        var presenter = new WebviewPresenter(session);

        var window = ShellWindowFactory.Create(options);
        var extensions = ApplyExtensions(window, context, options.Label);
        window.AddWebview(presenter);

        var entry = new WindowRegistry.Entry(window, session, context) { Webview = session };
        WireExtensionLifecycle(window, extensions);
        WireWindowEvents(window, options.Label, entry);
        return entry;
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

        // The close request flow is a cancellable hook with an explicit receipt channel: the shell
        // cancels the OS close and emits `window://close-requested`, then the front-end answers with
        // one of two receipts — `core:window|close` (force=true, sets ClosePending) to confirm, or
        // `core:window|deny-close` to cancel the scheduled force-close so the window stays open for
        // the next user gesture. If no receipt arrives, the configured fallback timeout force-closes
        // anyway so a hung web view cannot trap the user behind an unresponsive window.
        window.Closing += (_, eventArgs) =>
        {
            if (entry.ClosePending)
            {
                entry.CancelCloseFallback();
                return;
            }

            eventArgs.Cancel = true;
            var payload = JsonSerializer.SerializeToElement(
                new WindowLabelOptions(label),
                TaruiJsonContext.Default.WindowLabelOptions);
            FireAndForget.Run(_eventRouter.EmitToWindowAsync(label, "window://close-requested", payload));
            ScheduleCloseFallback(window, entry, label);
        };
        window.Closed += (_, _) =>
        {
            entry.CancelCloseFallback();
            FireAndForget.Run(HandleWindowClosedAsync(label, entry));
        };
    }

    private void ScheduleCloseFallback(Window window, WindowRegistry.Entry entry, string label)
    {
        var timeout = _lifecycleOptions.CloseRequestTimeout;
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        entry.CancelCloseFallback();
        var source = new CancellationTokenSource();
        entry.CloseFallback = source;
        var token = source.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(timeout, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (entry.ClosePending || token.IsCancellationRequested)
                {
                    return;
                }

                LogCloseRequestTimeoutExceeded(label, timeout);
                entry.ClosePending = true;
                try
                {
                    window.Close();
                }
                catch (Exception ex)
                {
                    LogCloseRequestForceCloseFailed(ex, label);
                }
            }).GetTask().ConfigureAwait(false);
        }, token);
    }

    private async ValueTask HandleWindowClosedAsync(string label, WindowRegistry.Entry entry)
    {
        if (!_registry.Remove(label))
        {
            return;
        }

        if (entry.Sink is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (entry.Sink is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _shutdownCoordinator.NotifyWindowClosed(label, _registry.Labels.Count);
        await _eventRouter.EmitToAllAsync(
            "window://destroyed",
            JsonSerializer.SerializeToElement(new WindowLabelOptions(label), TaruiJsonContext.Default.WindowLabelOptions));
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
