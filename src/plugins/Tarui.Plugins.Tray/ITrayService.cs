using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Tray;

/// <summary>
/// System tray operations. A tray is owned by the window that created it
/// (<paramref name="ownerWindow"/>); every subsequent update targets an owned tray and is rejected
/// when another window attempts to modify it.
/// </summary>
public interface ITrayService
{
    ValueTask<Unit> CreateAsync(string ownerWindow, TrayCreateOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> SetMenuAsync(string ownerWindow, TraySetMenuOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> SetIconAsync(string ownerWindow, TraySetIconOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> SetTooltipAsync(string ownerWindow, TraySetTooltipOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> SetVisibleAsync(string ownerWindow, TraySetVisibleOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> RemoveAsync(string ownerWindow, TrayRemoveOptions options, CancellationToken cancellationToken);
}