using System.Text.Json;
using Avalonia.Controls;
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

    public async ValueTask<Unit> RemoveWindowMenuAsync(string ownerWindow, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _menus.Remove(ownerWindow);
        _definitions.Remove(ownerWindow);
        ApplyMenu(ownerWindow, null);
        return new Unit();
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