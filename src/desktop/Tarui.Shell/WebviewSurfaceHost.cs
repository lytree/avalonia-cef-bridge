using Avalonia;
using Avalonia.Controls;

namespace Tarui.Shell;

/// <summary>
/// The web view content slot: a panel that fills surfaces without bounds and positions bounded ones at
/// their rect (logical units), so a window can present several web views in different regions while a
/// single unbounded surface keeps filling the whole window exactly as before.
/// </summary>
internal sealed class WebviewSurfaceHost : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0d;
        var height = 0d;
        foreach (var child in Children)
        {
            child.Measure(child is WebviewPresenter { SurfaceBounds: { } bounds }
                ? new Size(bounds.Width, bounds.Height)
                : availableSize);
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            child.Arrange(child is WebviewPresenter { SurfaceBounds: { } bounds }
                ? new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height)
                : new Rect(finalSize));
        }

        return finalSize;
    }
}
