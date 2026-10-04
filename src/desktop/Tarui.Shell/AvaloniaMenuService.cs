using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Menu;

namespace Tarui.Shell;

public sealed class AvaloniaMenuService(WindowRegistry registry, EventRouter events) : IMenuService
{
    private const string ItemClickedEvent = "menu://item-clicked";

    private readonly Dictionary<string, NativeMenu> _menus = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MenuItemDefinition[]> _definitions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lifecycleWired = new(StringComparer.Ordinal);

    public async ValueTask<Unit> SetWindowMenuAsync(
        string ownerWindow,
        SetWindowMenuOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativeMenuBuilder.ValidateUniqueIds(options.Items);

        var menu = Build(ownerWindow, options.Items);
        _menus[ownerWindow] = menu;
        _definitions[ownerWindow] = options.Items;
        ApplyMenu(ownerWindow, menu);
        WireLifecycle(ownerWindow);
        return new Unit();
    }

    public async ValueTask<Unit> UpdateItemAsync(
        string ownerWindow,
        MenuUpdateItemOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_definitions.TryGetValue(ownerWindow, out var items))
        {
            throw new InvalidOperationException($"No menu is set for window '{ownerWindow}'.");
        }

        if (!Find(items, options.Id, out var matched))
        {
            throw new InvalidOperationException($"Menu item '{options.Id}' was not found on window '{ownerWindow}'.");
        }

        var updated = Replace(items, options);
        var menu = Build(ownerWindow, updated);
        _definitions[ownerWindow] = updated;
        _menus[ownerWindow] = menu;
        ApplyMenu(ownerWindow, menu);
        return new Unit();
    }

    public async ValueTask<Unit> AppendAsync(
        string ownerWindow,
        MenuAppendOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_definitions.TryGetValue(ownerWindow, out var items))
        {
            throw new InvalidOperationException($"No menu is set for window '{ownerWindow}'.");
        }

        NativeMenuBuilder.ValidateUniqueIds(options.Items);
        var appended = Merge(items, options.Items);
        NativeMenuBuilder.ValidateUniqueIds(appended);
        Rebuild(ownerWindow, appended);
        return new Unit();
    }

    public async ValueTask<Unit> InsertAsync(
        string ownerWindow,
        MenuInsertOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_definitions.TryGetValue(ownerWindow, out var items))
        {
            throw new InvalidOperationException($"No menu is set for window '{ownerWindow}'.");
        }

        if (options.Index < 0 || options.Index > items.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Index,
                $"The insert index {options.Index} is outside the root menu of window '{ownerWindow}'.");
        }

        NativeMenuBuilder.ValidateUniqueIds(options.Items);
        var inserted = InsertAt(items, options.Index, options.Items);
        NativeMenuBuilder.ValidateUniqueIds(inserted);
        Rebuild(ownerWindow, inserted);
        return new Unit();
    }

    public async ValueTask<Unit> RemoveAsync(
        string ownerWindow,
        MenuRemoveOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_definitions.TryGetValue(ownerWindow, out var items))
        {
            throw new InvalidOperationException($"No menu is set for window '{ownerWindow}'.");
        }

        if (!RemoveById(items, options.Id, out var removed))
        {
            throw new InvalidOperationException($"Menu item '{options.Id}' was not found on window '{ownerWindow}'.");
        }

        Rebuild(ownerWindow, removed);
        return new Unit();
    }

    public async ValueTask<Unit> RemoveWindowMenuAsync(string ownerWindow, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _menus.Remove(ownerWindow);
        _definitions.Remove(ownerWindow);
        ApplyMenu(ownerWindow, null);
        return new Unit();
    }

    public async ValueTask<Unit> ShowContextMenuAsync(
        string ownerWindow,
        ContextMenuOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativeMenuBuilder.ValidateUniqueIds(options.Items);

        if (!registry.TryGet(ownerWindow, out var entry) || entry.Window is null)
        {
            return new Unit();
        }

        var window = entry.Window;
        if (Dispatcher.UIThread.CheckAccess())
        {
            OpenContextMenu(window, ownerWindow, options);
        }
        else
        {
            Dispatcher.UIThread.Post(() => OpenContextMenu(window, ownerWindow, options));
        }

        return new Unit();
    }

    private void OpenContextMenu(Window window, string windowLabel, ContextMenuOptions options)
    {
        var popup = NativeMenuBuilder.BuildContextMenuPopup(
            window,
            options.Items,
            options.X,
            options.Y,
            (id, text, isChecked) => EmitContextMenuClickedAsync(windowLabel, id, text, isChecked));
        popup.Open();
    }

    private async ValueTask EmitContextMenuClickedAsync(string windowLabel, string id, string? text, bool? isChecked)
    {
        await events.EmitToWindowAsync(
            windowLabel,
            ItemClickedEvent,
            JsonSerializer.SerializeToElement(new MenuItemClicked(id, text, isChecked), TaruiJsonContext.Default.MenuItemClicked));
    }

    private NativeMenu Build(string windowLabel, MenuItemDefinition[] items) =>
        NativeMenuBuilder.Build(
            items,
            (id, text, isChecked) => EmitItemClickedAsync(windowLabel, id, text, isChecked));

    private async ValueTask EmitItemClickedAsync(string windowLabel, string id, string? text, bool? isChecked)
    {
        await events.EmitToWindowAsync(
            windowLabel,
            ItemClickedEvent,
            JsonSerializer.SerializeToElement(new MenuItemClicked(id, text, isChecked), TaruiJsonContext.Default.MenuItemClicked));
    }

    private void Rebuild(string ownerWindow, MenuItemDefinition[] items)
    {
        var menu = Build(ownerWindow, items);
        _definitions[ownerWindow] = items;
        _menus[ownerWindow] = menu;
        ApplyMenu(ownerWindow, menu);
    }

    internal static MenuItemDefinition[] Merge(MenuItemDefinition[] items, MenuItemDefinition[] additions)
    {
        var result = new MenuItemDefinition[items.Length + additions.Length];
        items.CopyTo(result, 0);
        additions.CopyTo(result, items.Length);
        return result;
    }

    internal static MenuItemDefinition[] InsertAt(MenuItemDefinition[] items, int index, MenuItemDefinition[] additions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, items.Length);
        var result = new MenuItemDefinition[items.Length + additions.Length];
        items.CopyTo(result, 0);
        additions.CopyTo(result, index);
        items.AsSpan(index).CopyTo(result.AsSpan(index + additions.Length));
        return result;
    }

    internal static bool RemoveById(MenuItemDefinition[] items, string id, out MenuItemDefinition[] removed)
    {
        for (var index = 0; index < items.Length; index++)
        {
            var node = items[index];
            if (node.Kind != MenuItemKind.Divider && string.Equals(node.Id, id, StringComparison.Ordinal))
            {
                removed = [.. items[..index], .. items[(index + 1)..]];
                return true;
            }

            if (node.Items is { Length: > 0 } && RemoveById(node.Items, id, out var removedChild))
            {
                removed = [.. items[..index], node with { Items = removedChild }, .. items[(index + 1)..]];
                return true;
            }
        }

        removed = [];
        return false;
    }

    private static MenuItemDefinition[] Replace(MenuItemDefinition[] items, MenuUpdateItemOptions update)
    {
        var result = new MenuItemDefinition[items.Length];
        for (var index = 0; index < items.Length; index++)
        {
            result[index] = ReplaceNode(items[index], update);
        }

        return result;
    }

    private static MenuItemDefinition ReplaceNode(MenuItemDefinition node, MenuUpdateItemOptions update)
    {
        if (node.Kind != MenuItemKind.Divider && string.Equals(node.Id, update.Id, StringComparison.Ordinal))
        {
            return node with
            {
                Text = update.Text ?? node.Text,
                Enabled = update.Enabled ?? node.Enabled,
                Checked = update.Checked ?? node.Checked,
            };
        }

        if (node.Items is { Length: > 0 })
        {
            return node with { Items = Replace(node.Items, update) };
        }

        return node;
    }

    private static bool Find(MenuItemDefinition[] items, string id, out MenuItemDefinition matched)
    {
        foreach (var item in items)
        {
            if (item.Kind != MenuItemKind.Divider && string.Equals(item.Id, id, StringComparison.Ordinal))
            {
                matched = item;
                return true;
            }

            if (item.Items is { Length: > 0 } && Find(item.Items, id, out matched))
            {
                return true;
            }
        }

        matched = null!;
        return false;
    }

    private void ApplyMenu(string windowLabel, NativeMenu? menu)
    {
        if (!registry.TryGet(windowLabel, out var entry) || entry.Window is null)
        {
            return;
        }

        var window = entry.Window;
        if (Dispatcher.UIThread.CheckAccess())
        {
            SetMenu(window, menu);
        }
        else
        {
            Dispatcher.UIThread.Post(() => SetMenu(window, menu));
        }
    }

    private static void SetMenu(Window window, NativeMenu? menu) => NativeMenu.SetMenu(window, menu);

    private void WireLifecycle(string windowLabel)
    {
        if (!_lifecycleWired.Add(windowLabel))
        {
            return;
        }

        if (!registry.TryGet(windowLabel, out var entry) || entry.Window is null)
        {
            return;
        }

        entry.Window.Closed += (_, _) =>
        {
            _menus.Remove(windowLabel);
            _definitions.Remove(windowLabel);
        };
    }
}