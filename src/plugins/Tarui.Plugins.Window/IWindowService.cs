using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Window;

public interface IWindowService
{
    ValueTask<Unit> CreateAsync(WindowOptions options, CommandContext callerContext, CancellationToken cancellationToken);

    ValueTask<Unit> CloseAsync(string label, CancellationToken cancellationToken);

    ValueTask<Unit> MinimizeAsync(string label, CancellationToken cancellationToken);

    ValueTask<Unit> MaximizeAsync(string label, CancellationToken cancellationToken);

    ValueTask<Unit> UnmaximizeAsync(string label, CancellationToken cancellationToken);

    ValueTask<Unit> ToggleMaximizeAsync(string label, CancellationToken cancellationToken);

    ValueTask<Unit> HideAsync(string label, CancellationToken cancellationToken);

    ValueTask<Unit> ShowAsync(string label, CancellationToken cancellationToken);

    ValueTask<Unit> FocusAsync(string label, CancellationToken cancellationToken);

    ValueTask<Unit> CenterAsync(string label, CancellationToken cancellationToken);

    ValueTask<Unit> SetTitleAsync(string label, string title, CancellationToken cancellationToken);

    ValueTask<Unit> SetSizeAsync(string label, double width, double height, CancellationToken cancellationToken);

    ValueTask<Unit> SetPositionAsync(string label, double x, double y, CancellationToken cancellationToken);

    ValueTask<Unit> SetMinSizeAsync(string label, double? width, double? height, CancellationToken cancellationToken);

    ValueTask<Unit> SetMaxSizeAsync(string label, double? width, double? height, CancellationToken cancellationToken);

    ValueTask<Unit> SetAlwaysOnTopAsync(string label, bool value, CancellationToken cancellationToken);

    ValueTask<Unit> SetIconAsync(string label, byte[]? png, CancellationToken cancellationToken);

    ValueTask<Unit> SetThemeAsync(string label, string? theme, CancellationToken cancellationToken);

    ValueTask<Unit> SetAlwaysOnBottomAsync(string label, bool value, CancellationToken cancellationToken);

    ValueTask<Unit> SetShadowAsync(string label, bool value, CancellationToken cancellationToken);

    ValueTask<Unit> SetBadgeCountAsync(string label, int? count, CancellationToken cancellationToken);

    ValueTask<Unit> SetProgressBarAsync(string label, string? status, double? progress, CancellationToken cancellationToken);

    ValueTask<Unit> SetResizableAsync(string label, bool value, CancellationToken cancellationToken);

    ValueTask<Unit> SetDecorationsAsync(string label, bool value, CancellationToken cancellationToken);

    ValueTask<Unit> SetFullscreenAsync(string label, bool value, CancellationToken cancellationToken);

    /// <summary>
    /// Fullscreens the window on the monitor containing the physical point (<paramref name="x"/>,
    /// <paramref name="y"/>). A point outside every connected monitor is a no-op, matching Tauri.
    /// </summary>
    ValueTask<Unit> SetFullscreenOnMonitorAsync(string label, double x, double y, CancellationToken cancellationToken);

    ValueTask<WindowStateInfo> GetStateAsync(string label, CancellationToken cancellationToken);

    ValueTask<MonitorInfo?> GetCurrentMonitorAsync(string label, CancellationToken cancellationToken);

    ValueTask<MonitorInfo?> GetPrimaryMonitorAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(string label, CancellationToken cancellationToken);

    ValueTask<string[]> ListAsync(CancellationToken cancellationToken);
}
