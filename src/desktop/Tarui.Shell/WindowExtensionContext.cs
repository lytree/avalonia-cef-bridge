using System.Text.Json;
using Avalonia.Controls;
using Tarui.Ipc;

namespace Tarui.Shell;

/// <summary>
/// The per-window context handed to an <see cref="IShellWindowExtension"/> during window assembly. It carries
/// the window label, the capability-scoped command context (the authoritative validation source),
/// the composition facade, the scoped service provider for resolving collaborator services, and — naturally —
/// the native <see cref="Window"/> itself so an extension can manipulate the window (move, resize, show/hide,
/// top-most) and emit events back to the window's web view.
/// </summary>
public sealed class WindowExtensionContext
{
    private readonly EventRouter _eventRouter;

    internal WindowExtensionContext(
        string label,
        CommandContext context,
        ShellWindowComposition composition,
        IServiceProvider services,
        EventRouter eventRouter)
    {
        Label = label;
        Context = context;
        Composition = composition;
        Services = services;
        _eventRouter = eventRouter;
    }

    /// <summary>The label of the window being assembled.</summary>
    public string Label { get; }

    /// <summary>The capability-scoped context authorizing this window; treat as read-only authority.</summary>
    public CommandContext Context { get; }

    /// <summary>The layered composition facade onto which the extension mounts its controls.</summary>
    public ShellWindowComposition Composition { get; }

    /// <summary>The service provider used to resolve collaborator services for this window.</summary>
    public IServiceProvider Services { get; }

    /// <summary>The native Avalonia window the extension's controls are being mounted into.</summary>
    public Window Window => Composition.Window;

    /// <summary>
    /// Emits an event to this window's web view through the shared event router. Native controls use this to
    /// surface their own state changes (e.g. a sidebar selection) to the page that owns the content slot.
    /// </summary>
    public ValueTask EmitAsync(string eventName, JsonElement payload) =>
        _eventRouter.EmitToWindowAsync(Label, eventName, payload);
}