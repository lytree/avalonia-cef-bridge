namespace Tarui.Contracts;

/// <summary>
/// Identifies the allowed menu item kinds. <c>divider</c> renders a separator and ignores all
/// other fields; <c>check</c> renders a togglable check box; <c>submenu</c> hosts nested
/// <see cref="Items"/> under <see cref="Text"/>; <c>normal</c> is a plain clickable item.
/// </summary>
public static class MenuItemKind
{
    public const string Normal = "normal";
    public const string Divider = "divider";
    public const string Check = "check";
    public const string Submenu = "submenu";
}

/// <summary>
/// Declarative menu item definition. <see cref="Id"/> must be unique across the whole menu tree;
/// the shell uses it to route <c>menu://item-clicked</c> events and to address <c>update-item</c>.
/// <see cref="Accelerator"/> is display-only and never registered as a global hotkey.
/// </summary>
public sealed record MenuItemDefinition(
    string Id,
    string Kind = MenuItemKind.Normal,
    string? Text = null,
    bool? Enabled = null,
    bool? Checked = null,
    string? Accelerator = null,
    MenuItemDefinition[]? Items = null);

public sealed record SetWindowMenuOptions(MenuItemDefinition[] Items);

public sealed record MenuUpdateItemOptions(string Id, string? Text = null, bool? Enabled = null, bool? Checked = null);

public sealed record MenuItemClicked(string Id, string? Text = null, bool? Checked = null);

/// <summary>
/// Tray options for <c>plugin:tray|create</c>. The tray is owned by the window that creates it;
/// only that window may later update or remove it. <see cref="Icon"/> is optional; when set it is
/// a file path (optionally prefixed with a <c>base:</c> base directory, e.g. <c>resources:tray.ico</c>)
/// resolved and loaded by the shell.
/// </summary>
public sealed record TrayCreateOptions(
    string Id,
    string? Tooltip = null,
    bool Visible = true,
    string? Icon = null,
    bool ShowMenuOnLeftClick = true,
    MenuItemDefinition[]? Menu = null);

public sealed record TraySetMenuOptions(string Id, MenuItemDefinition[] Menu);

public sealed record TraySetIconOptions(string Id, string? Icon);

public sealed record TraySetTooltipOptions(string Id, string? Tooltip);

public sealed record TraySetVisibleOptions(string Id, bool Visible);

public sealed record TrayRemoveOptions(string Id);

public sealed record TrayClicked(string Id, string? Button = null);

public sealed record TrayMenuItemClicked(string Id, string ItemId, string? Text);