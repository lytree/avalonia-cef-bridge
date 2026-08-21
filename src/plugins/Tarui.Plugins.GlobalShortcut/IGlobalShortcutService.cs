using Tarui.Contracts;

namespace Tarui.Plugins.GlobalShortcut;

/// <summary>
/// Registers process-wide shortcuts that fire even when the application has no focus. A single
/// accelerator may be registered at most once; re-registering returns a stable failure. The
/// <c>global-shortcut://triggered</c> event is delivered to every window authorized to receive it.
/// </summary>
public interface IGlobalShortcutService
{
    ValueTask<GlobalShortcutState> RegisterAsync(GlobalShortcutOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> UnregisterAsync(GlobalShortcutOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> UnregisterAllAsync(CancellationToken cancellationToken);

    ValueTask<GlobalShortcutState> IsRegisteredAsync(GlobalShortcutOptions options, CancellationToken cancellationToken);
}