using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Tray;

namespace Tarui.Shell;

public sealed class AvaloniaTrayService(WindowRegistry registry, EventRouter events) : ITrayService, IDisposable
{
    private const string ClickedEvent = "tray://clicked";
    private const string MenuItemClickedEvent = "tray://menu-item-clicked";

    private readonly Dictionary<string, TrayHandle> _trays = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly HashSet<string> _lifecycleWired = new(StringComparer.Ordinal);

    public async ValueTask<Unit> CreateAsync(
        string ownerWindow,
        TrayCreateOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_trays.ContainsKey(options.Id))
            {
                throw new InvalidOperationException($"A tray icon with id '{options.Id}' already exists.");
            }
        }

        NativeMenuBuilder.ValidateUniqueIds(options.Menu ?? []);
        var icon = LoadIcon(options.Icon);
        var menu = options.Menu is { Length: > 0 }
            ? NativeMenuBuilder.Build(options.Menu, (itemId, text, isChecked) => EmitMenuClickedAsync(options.Id, ownerWindow, itemId, text))
            : null;

        var tray = new TrayIcon
        {
            Icon = icon,
            ToolTipText = options.Tooltip,
            Menu = menu,
            IsVisible = options.Visible,
        };
        tray.Clicked += (_, _) => FireAndForget(EmitClickedAsync(options.Id, ownerWindow, "Left"));

        var handle = new TrayHandle(ownerWindow, tray);
        lock (_gate)
        {
            _trays[options.Id] = handle;
        }

        RefreshApplicationIcons();
        WireLifecycle(ownerWindow);
        return new Unit();
    }

    public async ValueTask<Unit> SetMenuAsync(
        string ownerWindow,
        TraySetMenuOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handle = Owned(ownerWindow, options.Id, "set-menu");
        NativeMenuBuilder.ValidateUniqueIds(options.Menu);
        handle.Tray.Menu = NativeMenuBuilder.Build(
            options.Menu,
            (itemId, text, isChecked) => EmitMenuClickedAsync(options.Id, ownerWindow, itemId, text));
        return new Unit();
    }

    public async ValueTask<Unit> SetIconAsync(
        string ownerWindow,
        TraySetIconOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handle = Owned(ownerWindow, options.Id, "set-icon");
        handle.Tray.Icon = LoadIcon(options.Icon);
        return new Unit();
    }

    public async ValueTask<Unit> SetTooltipAsync(
        string ownerWindow,
        TraySetTooltipOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handle = Owned(ownerWindow, options.Id, "set-tooltip");
        handle.Tray.ToolTipText = options.Tooltip;
        return new Unit();
    }

    public async ValueTask<Unit> SetVisibleAsync(
        string ownerWindow,
        TraySetVisibleOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handle = Owned(ownerWindow, options.Id, "set-visible");
        handle.Tray.IsVisible = options.Visible;
        return new Unit();
    }

    public async ValueTask<Unit> RemoveAsync(
        string ownerWindow,
        TrayRemoveOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveCore(options.Id, ownerWindow);
        return new Unit();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _trays.Clear();
        }

        RefreshApplicationIcons();
    }

    private TrayHandle Owned(string ownerWindow, string id, string operation)
    {
        lock (_gate)
        {
            if (!_trays.TryGetValue(id, out var handle))
            {
                throw new InvalidOperationException($"No tray icon with id '{id}' exists.");
            }

            if (!string.Equals(handle.OwnerWindow, ownerWindow, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Window '{ownerWindow}' cannot {operation} tray '{id}' owned by window '{handle.OwnerWindow}'.");
            }

            return handle;
        }
    }

    private void RemoveCore(string id, string ownerWindow)
    {
        lock (_gate)
        {
            if (!_trays.TryGetValue(id, out var handle))
            {
                throw new InvalidOperationException($"No tray icon with id '{id}' exists.");
            }

            if (!string.Equals(handle.OwnerWindow, ownerWindow, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Window '{ownerWindow}' cannot remove tray '{id}' owned by window '{handle.OwnerWindow}'.");
            }

            _trays.Remove(id);
        }

        RefreshApplicationIcons();
    }

    private async ValueTask EmitClickedAsync(string id, string ownerWindow, string? button)
    {
        await events.EmitToWindowAsync(
            ownerWindow,
            ClickedEvent,
            JsonSerializer.SerializeToElement(new TrayClicked(id, button), TaruiJsonContext.Default.TrayClicked));
    }

    private async ValueTask EmitMenuClickedAsync(string id, string ownerWindow, string itemId, string? text)
    {
        await events.EmitToWindowAsync(
            ownerWindow,
            MenuItemClickedEvent,
            JsonSerializer.SerializeToElement(new TrayMenuItemClicked(id, itemId, text), TaruiJsonContext.Default.TrayMenuItemClicked));
    }

    private void WireLifecycle(string ownerWindow)
    {
        if (!_lifecycleWired.Add(ownerWindow))
        {
            return;
        }

        if (!registry.TryGet(ownerWindow, out var entry) || entry.Window is null)
        {
            return;
        }

        entry.Window.Closed += (_, _) =>
        {
            string[] owned;
            lock (_gate)
            {
                owned = [.. _trays.Where(pair => string.Equals(pair.Value.OwnerWindow, ownerWindow, StringComparison.Ordinal)).Select(static pair => pair.Key)];
            }

            foreach (var id in owned)
            {
                RemoveCore(id, ownerWindow);
            }
        };
    }

    private static WindowIcon? LoadIcon(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return null;
        }

        // P0-03 path C: enforce tray icon containment through the shared guard so UNC shares
        // and symlink escapes are rejected before any bitmap allocation. The default allow
        // preserves the legacy behaviour for callers that do not declare explicit scopes while
        // still rejecting obvious escape paths.
        var path = TrayPathGuard.EnsureTrayIconAuthorized(spec, TrayPathGuard.DefaultAllow(), []);
        try
        {
            return new WindowIcon(new Bitmap(path));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Tray icon '{spec}' could not be loaded (unsupported format or missing file: {exception.Message}).");
        }
    }

    private void RefreshApplicationIcons()
    {
        if (Application.Current is null)
        {
            return;
        }

        var icons = new TrayIcons();
        lock (_gate)
        {
            foreach (var handle in _trays.Values)
            {
                icons.Add(handle.Tray);
            }
        }

        TrayIcon.SetIcons(Application.Current, icons);
    }

    private static async void FireAndForget(ValueTask task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Tray notifications are best-effort.
        }
    }

    private sealed record TrayHandle(string OwnerWindow, TrayIcon Tray);
}

/// <summary>
/// Resolves a tray icon identifier (a plain file path or a <c>base:</c>-prefixed spec) to an
/// absolute path. Known bases: appData, appLocalData, appConfig, temp, resources.
/// </summary>
internal static class TrayIconPath
{
    private const string ResourcesBase = "resources";

    public static string Resolve(string spec)
    {
        var separator = spec.IndexOf(':');
        if (separator > 0 && separator < spec.Length - 1)
        {
            var baseName = spec[..separator];
            var relative = spec[(separator + 1)..];
            var root = RootFor(baseName);
            if (root is not null)
            {
                return Path.Combine(root, relative);
            }
        }

        if (Path.IsPathRooted(spec))
        {
            return spec;
        }

        throw new InvalidOperationException($"Tray icon spec '{spec}' is not rooted and has no known base prefix.");
    }

    private static string? RootFor(string baseName)
    {
        return baseName switch
        {
            "appData" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "appLocalData" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "appConfig" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "temp" => Path.GetTempPath(),
            ResourcesBase => AppContext.BaseDirectory,
            _ => null,
        };
    }
}