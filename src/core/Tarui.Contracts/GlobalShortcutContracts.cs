namespace Tarui.Contracts;

/// <summary>
/// Options for <c>plugin:global-shortcut|register</c> and <c>unregister</c>. <see cref="Accelerator"/>
/// is a normalized shortcut like <c>Ctrl+Shift+A</c>. Registering the same accelerator twice returns
/// a stable failure; accelators outside the window's capability scope are denied before reaching the OS.
/// </summary>
public sealed record GlobalShortcutOptions(string Accelerator);

/// <summary>Result of <c>plugin:global-shortcut|register</c>.</summary>
public sealed record GlobalShortcutState(bool Registered);

/// <summary>Payload of the <c>global-shortcut://triggered</c> event.</summary>
public sealed record GlobalShortcutTriggered(string Accelerator);