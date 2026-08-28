﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Window;

namespace Tarui.Shell;

public sealed class AvaloniaWindowService(
    WindowRegistry registry,
    Func<WindowOptions, CommandContext?, WindowRegistry.Entry> windowFactory,
    WindowOptions primaryWindowOptions) : IWindowService
{
    public async ValueTask<Unit> CreateAsync(WindowOptions options, CommandContext callerContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (registry.TryGet(options.Label, out _))
        {
            throw new InvalidOperationException($"A window with label '{options.Label}' already exists.");
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var entry = windowFactory(options, callerContext);
            registry.Add(options.Label, entry);
            if (options.Visible)
            {
                entry.Window.Show();
                entry.Window.Activate();
            }
        });
        return new Unit();
    }

    public async ValueTask<Unit> CloseAsync(string label, bool force, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var entry = registry.Get(label);
            if (force)
            {
                entry.ClosePending = true;
            }

            entry.Window.Close();
        });
        return new Unit();
    }

    public ValueTask<Unit> MinimizeAsync(string label, CancellationToken cancellationToken) =>
        RunWindowActionAsync(label, static window => window.WindowState = WindowState.Minimized, cancellationToken);

    public ValueTask<Unit> MaximizeAsync(string label, CancellationToken cancellationToken) =>
        RunWindowActionAsync(label, static window => window.WindowState = WindowState.Maximized, cancellationToken);

    public ValueTask<Unit> UnmaximizeAsync(string label, CancellationToken cancellationToken) =>
        RunWindowActionAsync(label, static window => window.WindowState = WindowState.Normal, cancellationToken);

    public ValueTask<Unit> ToggleMaximizeAsync(string label, CancellationToken cancellationToken) =>
        RunWindowActionAsync(
            label,
            static window => window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized,
            cancellationToken);

    public ValueTask<Unit> HideAsync(string label, CancellationToken cancellationToken) =>
        RunWindowActionAsync(label, static window => window.Hide(), cancellationToken);

    public ValueTask<Unit> ShowAsync(string label, CancellationToken cancellationToken) =>
        RunWindowActionAsync(label, static window => { window.Show(); window.Activate(); }, cancellationToken);

    public ValueTask<Unit> FocusAsync(string label, CancellationToken cancellationToken) =>
        RunWindowActionAsync(label, static window => window.Activate(), cancellationToken);

    public ValueTask<Unit> CenterAsync(string label, CancellationToken cancellationToken) =>
        RunWindowActionAsync(label, static window => CenterWindow(window), cancellationToken);

    public ValueTask<Unit> SetTitleAsync(string label, string title, CancellationToken cancellationToken) =>
        RunWindowActionAsync(label, window => window.Title = title, cancellationToken);

    public ValueTask<Unit> SetSizeAsync(string label, double width, double height, CancellationToken cancellationToken) =>
        RunWindowActionAsync(
            label,
            window =>
            {
                window.Width = width;
                window.Height = height;
            },
            cancellationToken);

    public ValueTask<Unit> SetPositionAsync(string label, double x, double y, CancellationToken cancellationToken) =>
        RunWindowActionAsync(
            label,
            window => window.Position = ToPhysical(window, x, y),
            cancellationToken);

    public ValueTask<Unit> SetMinSizeAsync(string label, double? width, double? height, CancellationToken cancellationToken) =>
        RunWindowActionAsync(
            label,
            window =>
            {
                window.MinWidth = width ?? 0;
                window.MinHeight = height ?? 0;
            },
            cancellationToken);

    public ValueTask<Unit> SetMaxSizeAsync(string label, double? width, double? height, CancellationToken cancellationToken) =>
        RunWindowActionAsync(
            label,
            window =>
            {
                window.MaxWidth = width ?? double.PositiveInfinity;
                window.MaxHeight = height ?? double.PositiveInfinity;
            },
            cancellationToken);

    public ValueTask<Unit> SetAlwaysOnTopAsync(string label, bool value, CancellationToken cancellationToken) =>
        RunWindowActionAsync(label, window => window.Topmost = value, cancellationToken);

    public ValueTask<Unit> SetResizableAsync(string label, bool value, CancellationToken cancellationToken) =>
        RunWindowActionAsync(label, window => window.CanResize = value, cancellationToken);

    public ValueTask<Unit> SetDecorationsAsync(string label, bool value, CancellationToken cancellationToken) =>
        RunWindowActionAsync(
            label,
            window => window.WindowDecorations = value ? WindowDecorations.Full : WindowDecorations.None,
            cancellationToken);

    public ValueTask<Unit> SetFullscreenAsync(string label, bool value, CancellationToken cancellationToken) =>
        RunWindowActionAsync(
            label,
            window => window.WindowState = value ? WindowState.FullScreen : WindowState.Normal,
            cancellationToken);

    public async ValueTask<WindowStateInfo> GetStateAsync(string label, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(() => BuildState(label, registry.Get(label).Window));
    }

    public async ValueTask<MonitorInfo?> GetCurrentMonitorAsync(string label, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = registry.Get(label).Window;
            var monitors = BuildMonitors(window, out var current);
            return current is null ? null : monitors.FirstOrDefault(monitor => monitor.IsCurrent);
        });
    }

    public async ValueTask<MonitorInfo?> GetPrimaryMonitorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = FirstWindow();
            if (window is null)
            {
                return null;
            }

            var monitors = BuildMonitors(window, out _);
            return monitors.FirstOrDefault(static monitor => monitor.IsPrimary);
        });
    }

    public async ValueTask<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(string label, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = registry.Get(label).Window;
            return BuildMonitors(window, out _);
        });
    }

    public ValueTask<string[]> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(registry.Labels.ToArray());
    }

    private async ValueTask<Unit> RunWindowActionAsync(
        string label,
        Action<Window> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() => action(registry.Get(label).Window));
        return new Unit();
    }

    private Window? FirstWindow()
    {
        // Prefer the main window, then fall back to whichever shell is registered, so monitor geometry
        // queries do not hard-code a single window label.
        var labels = registry.Labels;
        var label = labels.Contains(primaryWindowOptions.Label) ? primaryWindowOptions.Label : labels.FirstOrDefault();
        return label is null || !registry.TryGet(label, out var entry) ? null : entry.Window;
    }

    private static WindowStateInfo BuildState(string label, Window window)
    {
        var scale = window.RenderScaling;
        var position = window.Position;
        return new WindowStateInfo(
            label,
            window.Title ?? string.Empty,
            window.IsActive,
            window.WindowState == WindowState.FullScreen,
            window.WindowState == WindowState.Maximized,
            window.WindowState == WindowState.Minimized,
            window.IsVisible,
            window.WindowDecorations != WindowDecorations.None,
            window.CanResize,
            window.Topmost,
            ThemeNames.From(window.ActualThemeVariant),
            scale,
            new LogicalPosition(position.X / scale, position.Y / scale),
            new LogicalSize(window.Width, window.Height));
    }

    private static MonitorInfo[] BuildMonitors(Window window, out MonitorInfo? current)
    {
        current = null;
        var screens = window.Screens;
        if (screens is null || screens.ScreenCount == 0)
        {
            return [];
        }

        var currentScreen = screens.ScreenFromWindow(window);
        var all = screens.All;
        var monitors = new MonitorInfo[all.Count];
        for (var index = 0; index < all.Count; index++)
        {
            var screen = all[index];
            monitors[index] = ToMonitorInfo(screen, ReferenceEquals(screen, currentScreen), index);
        }

        current = monitors.FirstOrDefault(static monitor => monitor.IsCurrent);
        return monitors;
    }

    private static MonitorInfo ToMonitorInfo(Screen screen, bool isCurrent, int index)
    {
        var scale = screen.Scaling;
        return new MonitorInfo(
            screen.DisplayName ?? $"Display {index + 1}",
            new LogicalPosition(screen.Bounds.X / scale, screen.Bounds.Y / scale),
            new LogicalSize(screen.Bounds.Width / scale, screen.Bounds.Height / scale),
            new LogicalPosition(screen.WorkingArea.X / scale, screen.WorkingArea.Y / scale),
            new LogicalSize(screen.WorkingArea.Width / scale, screen.WorkingArea.Height / scale),
            scale,
            screen.IsPrimary,
            isCurrent);
    }

    private static PixelPoint ToPhysical(Window window, double x, double y) =>
        new(
            (int)Math.Round(x * window.RenderScaling),
            (int)Math.Round(y * window.RenderScaling));

    private static void CenterWindow(Window window)
    {
        var screens = window.Screens;
        if (screens is null || screens.ScreenCount == 0)
        {
            return;
        }

        var screen = screens.ScreenFromWindow(window) ?? screens.Primary ?? screens.All[0];
        var scale = screen.Scaling;
        var width = (int)Math.Round(window.Width * scale);
        var height = (int)Math.Round(window.Height * scale);
        window.Position = new PixelPoint(
            screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - width) / 2),
            screen.WorkingArea.Y + Math.Max(0, (screen.WorkingArea.Height - height) / 2));
    }
}

internal static class ThemeNames
{
    public static string From(ThemeVariant? variant) =>
        variant == ThemeVariant.Dark ? "dark"
        : variant == ThemeVariant.Light ? "light"
        : "system";
}
