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

    /// <summary>Appends items to the end of the owner window's root menu level.</summary>
    ValueTask<Unit> AppendAsync(string ownerWindow, MenuAppendOptions options, CancellationToken cancellationToken);

    /// <summary>Inserts items into the owner window's root menu level at the given index.</summary>
    ValueTask<Unit> InsertAsync(string ownerWindow, MenuInsertOptions options, CancellationToken cancellationToken);

    /// <summary>Removes the item with the given id (depth-first search) from the owner window's menu.</summary>
    ValueTask<Unit> RemoveAsync(string ownerWindow, MenuRemoveOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> RemoveWindowMenuAsync(string ownerWindow, CancellationToken cancellationToken);

    /// <summary>Pops a temporary context menu on the owner window at the requested coordinates.</summary>
    ValueTask<Unit> ShowContextMenuAsync(string ownerWindow, ContextMenuOptions options, CancellationToken cancellationToken);
}