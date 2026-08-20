namespace Tarui.Ipc;

public sealed record CommandContext(
    string WindowLabel,
    string WebViewLabel,
    CapabilitySet Capabilities);

public sealed class CommandNotFoundException(string command)
    : Exception($"Command '{command}' is not registered.");

public sealed class PermissionDeniedException(string command)
    : Exception($"Command '{command}' is not allowed for this capability.");

public sealed class InvalidPayloadException()
    : Exception("The command payload is invalid.");
