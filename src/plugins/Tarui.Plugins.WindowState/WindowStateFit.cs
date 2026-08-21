using Tarui.Contracts;

namespace Tarui.Plugins.WindowState;

/// <summary>
/// Pure geometry fitting that validates a persisted snapshot against the current display set.
/// Used by the restore path so a window whose saved position is off a disconnected monitor is moved
/// back into a visible work area. Kept dependency-free (no Avalonia) and unit-testable in isolation.
/// </summary>
public static class WindowStateFit
{
    /// <summary>
    /// Returns <paramref name="state"/> unchanged when its rectangle intersects any connected
    /// monitor's work area; otherwise returns a copy repositioned to the primary monitor's work-area
    /// top-left. When no monitor is available the snapshot passes through unchanged because there is
    /// nothing to validate against.
    /// </summary>
    public static WindowStateSnapshot ClampToMonitors(
        WindowStateSnapshot state,
        IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            return state;
        }

        foreach (var monitor in monitors)
        {
            if (Intersects(state, monitor))
            {
                return state;
            }
        }

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        return state with
        {
            X = primary.WorkAreaPosition.X,
            Y = primary.WorkAreaPosition.Y,
        };
    }

    private static bool Intersects(WindowStateSnapshot state, MonitorInfo monitor)
    {
        var wa = monitor.WorkAreaPosition;
        var size = monitor.WorkAreaSize;
        var overlap = Math.Min(state.X + state.Width, wa.X + size.Width)
                      - Math.Max(state.X, wa.X);
        var overlapHeight = Math.Min(state.Y + state.Height, wa.Y + size.Height)
                            - Math.Max(state.Y, wa.Y);
        return overlap > 0 && overlapHeight > 0;
    }
}