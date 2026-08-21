namespace Tarui.WebView.Abstractions;

/// <summary>Whether a CSS window region lets the window be dragged or blocks dragging.</summary>
public enum DraggableRegionKind
{
    /// <summary>Maps to <c>-webkit-app-region: drag</c>; pressing here moves the window.</summary>
    Drag,
    /// <summary>Maps to <c>-webkit-app-region: no-drag</c>; interactive controls on top inherit this.</summary>
    NoDrag,
}

/// <summary>
/// A logical, CSS-pixel rectangle that participates in title-bar dragging. Regions are reported by the
/// renderer via the <c>webview://drag-region-updated</c> contract and interpreted by
/// <see cref="DraggableRegionSelector"/>. Compared (not accumulated) with rect equality so repeated
/// updates with unchanged geometry are cheap to skip.
/// </summary>
public readonly record struct DraggableRegion(
    double X,
    double Y,
    double Width,
    double Height,
    DraggableRegionKind Kind)
{
    /// <summary>Whether point (<paramref name="px"/>, <paramref name="py"/>) falls inside this rectangle.</summary>
    public bool Contains(double px, double py) =>
        px >= X && px < X + Width && py >= Y && py < Y + Height;
}

/// <summary>
/// Pure hit-tester that answers "does dragging start here". A point is draggable when at least one
/// <see cref="DraggableRegionKind.Drag"/> region contains it and no
/// <see cref="DraggableRegionKind.NoDrag"/> region also contains it. NoDrag always wins because
/// interactive controls are painted above draggable chrome, matching the CSS
/// <c>-webkit-app-region</c> semantics. Empty <c>Width</c>/<c>Height</c> (degenerate) regions never match.
/// </summary>
public static class DraggableRegionSelector
{
    public static bool HitTest(IReadOnlyList<DraggableRegion> regions, double px, double py)
    {
        var draggable = false;
        foreach (var region in regions)
        {
            if (region.Width <= 0 || region.Height <= 0)
            {
                continue;
            }

            if (!region.Contains(px, py))
            {
                continue;
            }

            if (region.Kind == DraggableRegionKind.NoDrag)
            {
                return false;
            }

            draggable = true;
        }

        return draggable;
    }

    /// <summary>Whether a region update actually changes the current set (distinct by value).</summary>
    public static bool Differs(IReadOnlyList<DraggableRegion> current, IReadOnlyList<DraggableRegion> next) =>
        !current.SequenceEqual(next);
}