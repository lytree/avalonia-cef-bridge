using System.Windows.Input;
using Avalonia.Controls;
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


}