using Tarui.Contracts;
using Tarui.Plugins.Window;
using Tarui.Plugins.WindowState;

namespace Tarui.Shell;

/// <summary>
/// Shell implementation of <see cref="IWindowStateService"/> that reads a window's geometry through
/// <see cref="IWindowService"/>, persists it through <see cref="IWindowStateStore"/>, and applies a
/// persisted snapshot back to the window after clamping it against the current monitor set.
/// </summary>
public sealed class AvaloniaWindowStateService(IWindowService windows, IWindowStateStore store) : IWindowStateService
{
    public async ValueTask<Unit> SaveAsync(string windowLabel, CancellationToken cancellationToken)
    {
        var state = await windows.GetStateAsync(windowLabel, cancellationToken);
        var snapshot = new WindowStateSnapshot(
            windowLabel,
            state.Position.X,
            state.Position.Y,
            state.Size.Width,
            state.Size.Height,
            state.IsMaximized,
            state.IsFullscreen);
        await store.SaveAsync(windowLabel, snapshot, cancellationToken);
        return new Unit();
    }

    public async ValueTask<WindowStateRestoreResult> RestoreAsync(string windowLabel, CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadAsync(windowLabel, cancellationToken);
        if (snapshot is null)
        {
            return new WindowStateRestoreResult(false);
        }

        var monitors = await windows.GetMonitorsAsync(windowLabel, cancellationToken);
        var target = WindowStateFit.ClampToMonitors(snapshot, monitors);

        await windows.SetPositionAsync(windowLabel, target.X, target.Y, cancellationToken);
        await windows.SetSizeAsync(windowLabel, target.Width, target.Height, cancellationToken);
        if (target.IsFullscreen)
        {
            await windows.SetFullscreenAsync(windowLabel, true, cancellationToken);
        }
        else
        {
            await windows.SetFullscreenAsync(windowLabel, false, cancellationToken);
            if (target.IsMaximized)
            {
                await windows.MaximizeAsync(windowLabel, cancellationToken);
            }
            else
            {
                await windows.UnmaximizeAsync(windowLabel, cancellationToken);
            }
        }

        return new WindowStateRestoreResult(true);
    }

    public async ValueTask<Unit> ClearAsync(string windowLabel, CancellationToken cancellationToken)
    {
        await store.ClearAsync(windowLabel, cancellationToken);
        return new Unit();
    }
}