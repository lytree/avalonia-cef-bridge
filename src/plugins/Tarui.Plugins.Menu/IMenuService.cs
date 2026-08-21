using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Menu;

/// <summary>
/// Native window menu operations. Every method acts on the <paramref name="ownerWindow"/> that
/// the calling webview belongs to; a window can only ever manage its own menu (cross-window menu
/// management is intentionally unsupported in this phase).
/// </summary>
public interface IMenuService
{
    ValueTask<Unit> SetWindowMenuAsync(string ownerWindow, SetWindowMenuOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> UpdateItemAsync(string ownerWindow, MenuUpdateItemOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> RemoveWindowMenuAsync(string ownerWindow, CancellationToken cancellationToken);
}