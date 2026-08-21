using Tarui.Contracts;

namespace Tarui.Plugins.WindowState;

/// <summary>
/// Persists and restores a window's geometry and state (position, size, maximization, fullscreen).
/// Persisted state is clamped against the current monitor set during restore so a window is never
/// restored onto a display that has been disconnected.
/// </summary>
public interface IWindowStateService
{
    ValueTask<Unit> SaveAsync(string windowLabel, CancellationToken cancellationToken);

    ValueTask<WindowStateRestoreResult> RestoreAsync(string windowLabel, CancellationToken cancellationToken);

    ValueTask<Unit> ClearAsync(string windowLabel, CancellationToken cancellationToken);
}

/// <summary>Where per-window state snapshots are persisted for the process lifetime.</summary>
public interface IWindowStateStore
{
    ValueTask SaveAsync(string windowLabel, WindowStateSnapshot snapshot, CancellationToken cancellationToken);

    ValueTask<WindowStateSnapshot?> ReadAsync(string windowLabel, CancellationToken cancellationToken);

    ValueTask ClearAsync(string windowLabel, CancellationToken cancellationToken);
}