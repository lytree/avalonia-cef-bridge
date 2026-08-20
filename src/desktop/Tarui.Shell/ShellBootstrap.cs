using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Core;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.Events;
using Tarui.Plugins.System;
using Tarui.Plugins.Window;
using Tarui.WebView.Abstractions;

namespace Tarui.Shell;

public static class ShellBootstrap
{
    public static Window CreateWindow(ITaruiWebViewFactory webViewFactory, Uri source)
    {
        var registry = new WindowRegistry();
        var eventRouter = new EventRouter(registry, new EventHub());
        var capabilitiesByWindow = CapabilityLoader.Load(Path.Combine(AppContext.BaseDirectory, "capabilities"));
        if (!capabilitiesByWindow.TryGetValue("main", out var mainCapability))
        {
            throw new InvalidOperationException(
                "No capability file grants permissions to the 'main' window. Add capabilities/main.json.");
        }

        var commands = new CommandRouterBuilder();
        var registeredPermissions = new HashSet<string>(StringComparer.Ordinal);
        Action<string> registerPermission = permission => registeredPermissions.Add(permission);

        // Assigned after all plugins register; windows are only created afterwards,
        // so the window factory always observes the fully built dispatcher.
        IpcDispatcher? dispatcher = null;

        var windowService = new AvaloniaWindowService(
            registry,
            options => CreateEntry(webViewFactory, eventRouter, capabilitiesByWindow, mainCapability, options, source));
        var eventSender = new RoutedEventSender(eventRouter);
        var dialogService = new AvaloniaDialogService(registry);
        var clipboardService = new AvaloniaClipboardService(registry);

        CorePlugin.Register(commands, registerPermission);
        WindowPlugin.Register(commands, registerPermission, windowService);
        EventPlugin.Register(commands, registerPermission, eventSender);
        DialogPlugin.Register(commands, registerPermission, dialogService);
        SystemPlugin.Register(
            commands,
            registerPermission,
            new PathService(),
            new OsService(),
            new ProcessService(),
            new ShellService(),
            clipboardService);

        var missingPermissions = capabilitiesByWindow.Values
            .SelectMany(static capability => capability.Permissions)
            .Where(permission => permission != "*" && !registeredPermissions.Contains(permission))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (missingPermissions.Length > 0)
        {
            throw new InvalidOperationException(
                $"Capability files reference unregistered permissions: {string.Join(", ", missingPermissions)}");
        }

        var router = commands.Build();
        dispatcher = new IpcDispatcher(router);

        WindowRegistry.Entry CreateEntry(
            ITaruiWebViewFactory factory,
            EventRouter router2,
            IReadOnlyDictionary<string, CapabilitySet> capabilities,
            CapabilitySet fallback,
            WindowOptions options,
            Uri mainSource)
        {
            var capability = capabilities.TryGetValue(options.Label, out var configured) ? configured : fallback;
            var context = new CommandContext(options.Label, options.Label, capability);
            var host = new WebViewHost(factory, dispatcher!, context, ResolveSource(options.Url, mainSource));
            var window = new ShellWindow(host, options);
            var entry = new WindowRegistry.Entry(window, host, context);
            WireWindowEvents(registry, router2, options.Label, entry);
            return entry;
        }

        var mainEntry = CreateEntry(
            webViewFactory,
            eventRouter,
            capabilitiesByWindow,
            mainCapability,
            new WindowOptions("main")
            {
                Title = "tarui.net",
                Width = 1280,
                Height = 820,
                MinWidth = 900,
                MinHeight = 600,
                Center = true,
            },
            source);
        registry.Add("main", mainEntry);

        if (Application.Current is { } application)
        {
            application.ActualThemeVariantChanged += (_, _) =>
                FireAndForget(eventRouter.EmitToAllAsync(
                    "shell://theme-changed",
                    JsonSerializer.SerializeToElement(
                        new ThemeChanged(ThemeNames.From(application.ActualThemeVariant)),
                        TaruiJsonContext.Default.ThemeChanged)));
        }

        return mainEntry.Window;
    }

    private static void WireWindowEvents(
        WindowRegistry registry,
        EventRouter eventRouter,
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

    private sealed class RoutedEventSender(EventRouter router) : IEventSender
    {
        public async ValueTask<Unit> EmitAsync(
            string eventName,
            JsonElement payload,
            string? targetWindow,
            CancellationToken cancellationToken)
        {
            await router.EmitAsync(eventName, payload, targetWindow, cancellationToken);
            return new Unit();
        }
    }
}
