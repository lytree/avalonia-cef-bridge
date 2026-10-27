using Avalonia;

namespace Tarui.Shell;

/// <summary>
/// Resolves which monitor owns a physical point for <c>core:window|set-fullscreen-on-monitor</c>.
/// Tauri resolves the target the same way — the monitor whose bounds contain the point — and does
/// nothing when no monitor matches, so there is deliberately no "nearest monitor" fallback.
/// </summary>
internal static class FullscreenTarget
{
    /// <summary>
    /// Returns the index of the first monitor rectangle containing <paramref name="point"/>, or
    /// <c>-1</c> when the point falls outside every connected monitor.
    /// </summary>
    public static int Resolve(IReadOnlyList<PixelRect> monitorBounds, PixelPoint point)
    {
        for (var index = 0; index < monitorBounds.Count; index++)
        {
            if (monitorBounds[index].Contains(point))
            {
                return index;
            }
        }

        return -1;
    }
}
