using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Positioner;

namespace Tarui.Shell;

/// <summary>
/// Avalonia-backed positioner. Resolves the target window from the <see cref="WindowRegistry"/>, takes the
/// work area of the screen the window currently sits on (falling back to the primary screen like
/// <see cref="AvaloniaWindowService.CenterWindow"/>), and lets <see cref="PositionerCalculator"/> compute the
/// final top-left position. <c>Tray*</c> anchors degrade to bottom-of-work-area slots because this platform
/// stack exposes no taskbar rectangle (see <see cref="PositionerCalculator"/>).
/// </summary>
public sealed class AvaloniaPositionerService(WindowRegistry registry) : IPositionerService
{
    public async ValueTask<Unit> SetPositionAsync(PositionerOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!PositionerAnchors.TryParse(options.Anchor, out var anchor))
        {
            throw new InvalidOperationException($"Unknown positioner anchor '{options.Anchor}'.");
        }

        var label = options.Label
            ?? throw new InvalidOperationException("The positioner command requires a window label.");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = registry.Get(label).Window;
            var screens = window.Screens;
            if (screens is null || screens.ScreenCount == 0)
            {
                // No monitor geometry available: the anchor cannot be computed, so leave the window in place.
                return;
            }

            var screen = screens.ScreenFromWindow(window) ?? screens.Primary ?? screens.All[0];
            var workArea = screen.WorkingArea;
            var scale = screen.Scaling;
            var (x, y) = PositionerCalculator.Calculate(
                anchor,
                workArea.X,
                workArea.Y,
                workArea.Width,
                workArea.Height,
                window.Width * scale,
                window.Height * scale,
                options.X ?? 0,
                options.Y ?? 0);
            window.Position = new PixelPoint(x, y);
        });
        return new Unit();
    }
}
