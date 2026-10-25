namespace Tarui.Ipc;

/// <summary>
/// The wire-level error codes the bridge returns to the frontend. These strings are part of the
/// frozen IPC contract: the guest <c>invoke</c> rejects with them and the contract snapshot pins
/// them, so renaming one is a reviewed contract change, not a refactor.
/// </summary>
public static class TaruiErrorCodes
{
    /// <summary>The requested command is not registered on the router.</summary>
    public const string CommandNotFound = "COMMAND_NOT_FOUND";

    /// <summary>The invoking surface's capability does not authorize the command.</summary>
    public const string PermissionDenied = "PERMISSION_DENIED";

    /// <summary>The envelope could not be parsed as an IPC invocation at all.</summary>
    public const string InvalidMessage = "INVALID_MESSAGE";

    /// <summary>The bridge itself failed before or while dispatching (session disposed, encode error).</summary>
    public const string BridgeError = "BRIDGE_ERROR";
}
