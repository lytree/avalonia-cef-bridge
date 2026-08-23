using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class WebviewAttacher(
    IServiceProvider services,
    WindowRegistry registry,
    EventRouter eventRouter,
    WebViewRequestPolicy requestPolicy,
    WindowCapabilityResolver capabilityResolver,
    TaruiAppOrigin appOrigin,
    IAppShutdownCoordinator shutdownCoordinator)
{
    public WindowRegistry.Entry Attach(WindowOptions options, CommandContext? callerContext = null)
    {
        var capability = callerContext is null
            ? capabilityResolver.Resolve(options.Label)
            : capabilityResolver.ResolveForCreate(options.Label, callerContext);
        var context = new CommandContext(options.Label, options.Label, capability);

        // The web view factory and dispatcher resolve lazily: windows are only assembled after the
        // dispatcher is fully built, so a window can be created even if a web view backend is absent.
        var dispatcher = services.GetRequiredService<IpcDispatcher>();
        var webViewFactory = services.GetRequiredService<ITaruiAvaloniaWebViewFactory>();

        var session = new WebviewSession(
            webViewFactory,
            dispatcher,
            eventRouter,
            requestPolicy,
            context,
            ResolveSource(options.Url, appOrigin));
        var presenter = new WebviewPresenter(session);

        var window = ShellWindowFactory.Create(options);
        window.AddWebview(presenter);

        var entry = new WindowRegistry.Entry(window, session, context) { Webview = session };
        WireWindowEvents(registry, eventRouter, shutdownCoordinator, options.Label, entry);
        return entry;
    }

    private static void WireWindowEvents(
        WindowRegistry registry,
        EventRouter eventRouter,
        IAppShutdownCoordinator shutdownCoordinator,
        string label,
        WindowRegistry.Entry entry)
    {
        var window = entry.Window;
        window.PositionChanged += (_, _) => FireAndForget(eventRouter.EmitToWindowAsync(
            label,
            "window://moved",
            JsonSerializer.SerializeToElement(BuildGeometry(window), TaruiJsonContext.Default.WindowGeometry)));
        window.Resized += (_, _) => FireAndForget(eventRouter.EmitToWindowAsync(
            label,
            "window://resized",
            JsonSerializer.SerializeToElement(BuildGeometry(window), TaruiJsonContext.Default.WindowGeometry)));
        window.Activated += (_, _) => FireAndForget(eventRouter.EmitToWindowAsync(
            label,
            "window://focus-changed",
            JsonSerializer.SerializeToElement(new WindowFocusChanged(true), TaruiJsonContext.Default.WindowFocusChanged)));
        window.Deactivated += (_, _) => FireAndForget(eventRouter.EmitToWindowAsync(
            label,
            "window://focus-changed",
            JsonSerializer.SerializeToElement(new WindowFocusChanged(false), TaruiJsonContext.Default.WindowFocusChanged)));
        window.Closing += (_, eventArgs) =>
        {
            if (entry.ClosePending)
            {
                return;
            }

            eventArgs.Cancel = true;
            FireAndForget(eventRouter.EmitToWindowAsync(
                label,
                "window://close-requested",
                JsonSerializer.SerializeToElement(new WindowLabelOptions(label), TaruiJsonContext.Default.WindowLabelOptions)));
        };
        window.Closed += (_, _) => FireAndForget(HandleWindowClosedAsync(
            registry,
            eventRouter,
            shutdownCoordinator,
            label,
            entry));
    }

    private static async ValueTask HandleWindowClosedAsync(
        WindowRegistry registry,
        EventRouter eventRouter,
        IAppShutdownCoordinator shutdownCoordinator,
        string label,
        WindowRegistry.Entry entry)
    {
        if (!registry.Remove(label))
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

        shutdownCoordinator.NotifyWindowClosed(label, registry.Labels.Count);
        await eventRouter.EmitToAllAsync(
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

    private static async void FireAndForget(ValueTask task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Window events are best-effort notifications.
        }
    }
}