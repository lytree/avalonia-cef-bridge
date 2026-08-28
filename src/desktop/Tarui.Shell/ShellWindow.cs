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
    private readonly bool _centerOnStart;

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
        _centerOnStart = options.Center;
        // Honor WindowOptions.Center: when the caller explicitly opts out of centering, fall back to
        // platform default placement instead of forcing the window onto the primary screen. When
        // centering is enabled (the default), the existing rule applies: skip centering only when the
        // caller pinned both X and Y. Avalonia's Manual mode without an explicit Position produces a
        // platform default placement, which is exactly the behaviour Center=false wants.
        WindowStartupLocation = !options.Center
            ? WindowStartupLocation.Manual
            : options.X is null || options.Y is null
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

    /// <summary>Reports whether the window was created with <see cref="WindowOptions.Center"/> enabled.</summary>
    public bool CenteredOnStart => _centerOnStart;

    /// <summary>Mounts a web view surface onto the composition's content slot.</summary>
    public void AddWebview(WebviewPresenter presenter)
    {
        Composition.Content.Children.Add(presenter);
    }
}
