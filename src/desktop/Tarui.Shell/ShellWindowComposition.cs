using Avalonia;
using Avalonia.Controls;

namespace Tarui.Shell;

/// <summary>
/// The layered desktop composition that hosts a web view surface together with additional native
/// Avalonia controls. It is the facade that <see cref="IShellWindowExtension"/> implementations interact
/// with: native chrome, four-sided docks, the web view content slot and a top-most overlay are exposed
/// as discrete regions, keeping the window itself free of layout logic.
/// </summary>
public sealed class ShellWindowComposition
{
    private readonly Grid _root;

    /// <summary>Native chrome region; intended for custom title bars when system decorations are off.</summary>
    public Panel Chrome { get; } = new();

    /// <summary>Four-sided dock region. Native panels are docked here; <see cref="Content"/> fills the rest.</summary>
    public DockPanel Docks { get; } = new();

    /// <summary>The web view content slot. Extensions must not replace or clear its children.</summary>
    public Panel Content { get; } = new();

    /// <summary>Top-most overlay region spanning the whole window; hit-testing is disabled by default.</summary>
    public Panel Overlay { get; } = new() { IsHitTestVisible = false };

    /// <summary>The owning window; assigned once the window is constructed.</summary>
    public Window Window { get; internal set; } = null!;

    /// <summary>Docks a native control onto one of the four sides of the dock region, ahead of the web view content.</summary>
    public void Dock(Control control, Dock placement)
    {
        DockPanel.SetDock(control, placement);
        var index = Docks.Children.IndexOf(Content);
        if (index >= 0)
        {
            Docks.Children.Insert(index, control);
        }
        else
        {
            Docks.Children.Add(control);
        }
    }

    internal Panel Root => _root;

    public ShellWindowComposition()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        // Top strip: native chrome. Collapses to zero height when empty.
        Grid.SetRow(Chrome, 0);
        Grid.SetColumn(Chrome, 0);
        _root.Children.Add(Chrome);

        // Middle: the dock area whose remaining space is filled by the web view content slot.
        Docks.Children.Add(Content);
        Grid.SetRow(Docks, 1);
        Grid.SetColumn(Docks, 0);
        _root.Children.Add(Docks);

        // Top-most overlay spanning both rows so it can render over chrome and docks alike.
        Grid.SetRow(Overlay, 0);
        Grid.SetColumn(Overlay, 0);
        Grid.SetRowSpan(Overlay, 2);
        _root.Children.Add(Overlay);
    }
}