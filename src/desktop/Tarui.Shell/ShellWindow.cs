using Avalonia;
using Avalonia.Controls;
using Tarui.Contracts;

namespace Tarui.Shell;

/// <summary>
/// A window that owns the shell geometry and the content slot (a grid) onto which one or more
/// <see cref="WebviewPresenter"/> surfaces are mounted. The window itself knows nothing about the
/// web view, IPC or capabilities — it is a pure native window whose display is supplied from outside.
/// </summary>
public sealed class ShellWindow : Window
{
    private readonly double? _pendingX;
    private readonly double? _pendingY;

    /// <summary>The single-cell content slot; web view surfaces are mounted onto it.</summary>
    public Grid Surface { get; } = new()
    {
        RowDefinitions = { new RowDefinition(GridLength.Star) },
        ColumnDefinitions = { new ColumnDefinition(GridLength.Star) },
    };

    public ShellWindow(WindowOptions options)
    {
        Title = options.Title;
        Width = options.Width;
        Height = options.Height;
        MinWidth = options.MinWidth ?? 0;
        MinHeight = options.MinHeight ?? 0;
        MaxWidth = options.MaxWidth ?? double.PositiveInfinity;
        MaxHeight = options.MaxHeight ?? double.PositiveInfinity;
        CanResize = options.Resizable;
        Topmost = options.AlwaysOnTop;
        WindowDecorations = options.Decorations ? WindowDecorations.Full : WindowDecorations.None;
        _pendingX = options.X;
        _pendingY = options.Y;
        WindowStartupLocation = options.X is null || options.Y is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.Manual;
        Content = Surface;

        if (_pendingX is not null && _pendingY is not null)
        {
            Opened += (_, _) =>
                Position = new PixelPoint(
                    (int)Math.Round(_pendingX.Value * RenderScaling),
                    (int)Math.Round(_pendingY.Value * RenderScaling));
        }
    }

    /// <summary>Mounts a web view surface onto the content slot at the given grid position.</summary>
    public void AddWebview(WebviewPresenter presenter, int column = 0, int row = 0)
    {
        Grid.SetColumn(presenter, column);
        Grid.SetRow(presenter, row);
        Surface.Children.Add(presenter);
    }
}