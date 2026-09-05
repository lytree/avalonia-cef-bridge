using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Shell;

/// <summary>
/// Builds an Avalonia <see cref="NativeMenu"/> from the declarative <see cref="MenuItemDefinition"/>
/// contract and validates that menu item ids are unique across the whole tree. Clicking an item
/// invokes <paramref name="click"/>, letting the caller route <c>menu://item-clicked</c>.
/// </summary>
internal static class NativeMenuBuilder
{
    public static void ValidateUniqueIds(MenuItemDefinition[] items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Walk(MenuItemDefinition[] nodes)
        {
            foreach (var node in nodes)
            {
                if (!string.IsNullOrEmpty(node.Id) && !seen.Add(node.Id))
                {
                    throw new InvalidOperationException($"Duplicate menu item id '{node.Id}'.");
                }

                if (node.Items is { Length: > 0 })
                {
                    Walk(node.Items);
                }
            }
        }

        Walk(items);
    }

    public static NativeMenu Build(
        MenuItemDefinition[] items,
        Func<string, string?, bool?, ValueTask> click)
    {
        var menu = new NativeMenu();
        foreach (var item in items)
        {
            menu.Add(BuildItem(item, click));
        }

        return menu;
    }

    /// <summary>
    /// Builds a lightweight context-menu <see cref="Popup"/> (hosting a widget <see cref="Menu"/> built from the
    /// same declarative items) positioned at <paramref name="x"/>/<paramref name="y"/> relative to
    /// <paramref name="target"/>. Item activation routes through <paramref name="click"/> and closes the popup.
    /// </summary>
    public static Popup BuildContextMenuPopup(
        Window target,
        MenuItemDefinition[] items,
        double x,
        double y,
        Func<string, string?, bool?, ValueTask> click)
    {
        var popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Top,
            HorizontalOffset = x,
            VerticalOffset = y,
        };
        var menu = new Menu();
        AddWidgets(menu.Items, items, click, popup);
        popup.Child = menu;
        return popup;
    }

    private static void AddWidgets(
        Avalonia.Controls.ItemCollection destination,
        MenuItemDefinition[] items,
        Func<string, string?, bool?, ValueTask> click,
        Popup popup)
    {
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case MenuItemKind.Divider:
                    destination.Add(new Separator());
                    break;

                case MenuItemKind.Submenu:
                {
                    var sub = new MenuItem { Header = item.Text ?? string.Empty, IsEnabled = item.Enabled ?? true };
                    AddWidgets(sub.Items, item.Items ?? [], click, popup);
                    destination.Add(sub);
                    break;
                }

                case MenuItemKind.Check:
                {
                    var check = new MenuItem
                    {
                        Header = item.Text ?? string.Empty,
                        IsEnabled = item.Enabled ?? true,
                        IsChecked = item.Checked ?? false,
                        ToggleType = MenuItemToggleType.CheckBox,
                    };
                    check.Command = new WidgetClickCommand(check, item.Id, click, popup);
                    destination.Add(check);
                    break;
                }

                default:
                {
                    var normal = new MenuItem { Header = item.Text ?? string.Empty, IsEnabled = item.Enabled ?? true };
                    normal.Command = new WidgetClickCommand(normal, item.Id, click, popup);
                    destination.Add(normal);
                    break;
                }
            }
        }
    }

    private static NativeMenuItemBase BuildItem(MenuItemDefinition definition, Func<string, string?, bool?, ValueTask> click)
    {
        switch (definition.Kind)
        {
            case MenuItemKind.Divider:
                return new NativeMenuItemSeparator();

            case MenuItemKind.Submenu:
            {
                var node = new NativeMenuItem(definition.Text ?? string.Empty)
            {
                IsEnabled = definition.Enabled ?? true,
            };
            node.Menu = Build(definition.Items ?? [], click);
            return node;
            }

            case MenuItemKind.Check:
            {
                var check = new NativeMenuItem(definition.Text ?? string.Empty)
                {
                    IsEnabled = definition.Enabled ?? true,
                    IsChecked = definition.Checked ?? false,
                    ToggleType = MenuItemToggleType.CheckBox,
                };
                check.Command = new MenuClickCommand(check, definition.Id, click);
                SetGesture(check, definition.Accelerator);
                return check;
            }

            default:
            {
                var normal = new NativeMenuItem(definition.Text ?? string.Empty)
                {
                    IsEnabled = definition.Enabled ?? true,
                };
                normal.Command = new MenuClickCommand(normal, definition.Id, click);
                SetGesture(normal, definition.Accelerator);
                return normal;
            }
        }
    }

    private static void SetGesture(NativeMenuItem item, string? accelerator)
    {
        if (string.IsNullOrWhiteSpace(accelerator))
        {
            return;
        }

        try
        {
            item.Gesture = KeyGesture.Parse(accelerator);
        }
        catch
        {
            // Accelerators are display-only; silently ignore malformed hints.
        }
    }

    /// <summary>
    /// Emits the item-clicked callback when the associated native item is activated. For check items
    /// the live <see cref="NativeMenuItem.IsChecked"/> state is read so the event reflects the toggle.
    /// </summary>
    private sealed class MenuClickCommand(NativeMenuItem item, string id, Func<string, string?, bool?, ValueTask> click) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            var isChecked = item.ToggleType != MenuItemToggleType.CheckBox
                ? null
                : (bool?)item.IsChecked;
            FireAndForget.Run(click(id, item.Header, isChecked));
        }
    }

    /// <summary>
    /// Routes a widget <see cref="MenuItem"/> activation through <paramref name="click"/> and closes the
    /// hosting context-menu <see cref="Popup"/>. For check items the live toggle state is reported.
    /// </summary>
    private sealed class WidgetClickCommand(MenuItem item, string id, Func<string, string?, bool?, ValueTask> click, Popup popup) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            var isChecked = item.ToggleType != MenuItemToggleType.CheckBox
                ? null
                : (bool?)item.IsChecked;
            FireAndForget.Run(click(id, item.Header?.ToString(), isChecked));
            popup.Close();
        }
    }


}