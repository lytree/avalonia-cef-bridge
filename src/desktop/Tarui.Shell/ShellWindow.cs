using Avalonia;
using Avalonia.Controls;
using Tarui.Contracts;

namespace Tarui.Shell;

/// <summary>
/// A window that owns the shell geometry and a <see cref="ShellWindowComposition"/> onto which the web
/// view surface and any native extension controls are mounted. The window itself knows nothing about the
/// web view, IPC or capabilities — it is a pure native window whose display is supplied from outside.
/// </summary>
public sealed class ShellWindow : Window
{
    private readonly double? _pendingX;
    private readonly double? _pendingY;

    /// <summary>The layered desktop composition: chrome, docks, the web view content slot and an overlay.</summary>
    public ShellWindowComposition Composition { get; }

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
        Composition = new ShellWindowComposition();
        Composition.Window = this;
        Content = Composition.Root;

        if (_pendingX is not null && _pendingY is not null)
        {
            Opened += (_, _) =>
                Position = new PixelPoint(
                    (int)Math.Round(_pendingX.Value * RenderScaling),
                    (int)Math.Round(_pendingY.Value * RenderScaling));
        }
    }

    /// <summary>Mounts a web view surface onto the composition's content slot.</summary>
    public void AddWebview(WebviewPresenter presenter)
    {
        Composition.Content.Children.Add(presenter);
    }
}