using Avalonia;
using Avalonia.Controls;
using Tarui.Contracts;

namespace Tarui.Shell;

public sealed class ShellWindow : Window
{
    private readonly double? _pendingX;
    private readonly double? _pendingY;

    public ShellWindow(WebViewHost webViewHost, WindowOptions options)
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
        Content = webViewHost;

        if (_pendingX is not null && _pendingY is not null)
        {
            Opened += (_, _) =>
                Position = new PixelPoint(
                    (int)Math.Round(_pendingX.Value * RenderScaling),
                    (int)Math.Round(_pendingY.Value * RenderScaling));
        }
    }
}
