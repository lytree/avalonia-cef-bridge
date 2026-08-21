using System.Text.Json;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.WebView.Abstractions;

namespace Tarui.Shell;

public sealed class ShellWindowFactory(
    IServiceProvider services,
    WindowRegistry registry,
    EventRouter eventRouter,
    WebViewRequestPolicy requestPolicy,
    WindowCapabilityResolver capabilityResolver,
    TaruiAppOrigin appOrigin,
    IAppShutdownCoordinator shutdownCoordinator)
{
    public WindowRegistry.Entry CreateEntry(WindowOptions options, CommandContext? callerContext = null)
    {
        var capability = callerContext is null
            ? capabilityResolver.Resolve(options.Label)
            : capabilityResolver.ResolveForCreate(options.Label, callerContext);
        var context = new CommandContext(options.Label, options.Label, capability);

        // The dispatcher and web view factory resolve lazily; windows are only created
        // after the dispatcher is fully built, so they always observe it.
        var dispatcher = services.GetRequiredService<IpcDispatcher>();
        var host = new WebViewHost(
            services.GetRequiredService<ITaruiWebViewFactory>(),
            dispatcher,
            eventRouter,
            requestPolicy,
            context,
            ResolveSource(options.Url, appOrigin.StartUri));
        var window = new ShellWindow(host, options);
        var entry = new WindowRegistry.Entry(window, host, context);
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
        window.Closed += (_, _) =>
        {
            registry.Remove(label);
            if (entry.Sink is IDisposable disposable)
            {
                disposable.Dispose();
            }

            shutdownCoordinator.NotifyWindowClosed(label, registry.Labels.Count);
            FireAndForget(eventRouter.EmitToAllAsync(
                "window://destroyed",
                JsonSerializer.SerializeToElement(new WindowLabelOptions(label), TaruiJsonContext.Default.WindowLabelOptions)));
        };
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

    private static Uri ResolveSource(string? url, Uri mainSource)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return mainSource;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            if (!string.Equals(absolute.Scheme, mainSource.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The URL scheme '{absolute.Scheme}' does not match the application origin '{mainSource.Scheme}'.");
            }

            return absolute;
        }

        return new Uri(mainSource, url);
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
