namespace Tarui.Ipc;

public sealed record CommandContext(
    string WindowLabel,
    string WebViewLabel,
    CapabilitySet Capabilities);

public sealed class CommandNotFoundException(string command)
    : Exception($"Command '{command}' is not registered.");

public sealed class PermissionDeniedException(string command)
    : Exception($"Command '{command}' is not allowed for this capability.");

public sealed class ScopeDeniedException(string command)
    : Exception($"Command '{command}' is denied by its capability scope.");

/// <summary>
/// Thrown when a window has no explicitly declared capability profile. A window must never
/// silently fall back to another window's (potentially more privileged) capability set.
/// </summary>
public sealed class CapabilityNotFoundException(string label)
    : Exception($"No capability profile is declared for window '{label}'.");

public sealed class InvalidPayloadException()
    : Exception("The command payload is invalid.");

/// <summary>
/// Thrown when Web code attempts to emit an event that is not in the <c>user://</c> namespace.
/// Native event prefixes are reserved for events emitted by the shell itself.
/// </summary>
public sealed class EventNamespaceDeniedException(string eventName)
    : Exception($"Web events must use the '{EventNames.UserNamespace}' namespace; '{eventName}' is reserved or invalid.");
