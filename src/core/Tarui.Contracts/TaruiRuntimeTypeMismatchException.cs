namespace Tarui.Contracts;

/// <summary>
/// Raised when host code asks for a runtime-specific extension the selected web view runtime does
/// not provide. Mirrors Tauri v3's <c>RuntimeTypeMismatch</c> error variant (T2): the runtime-specific
/// surface is isolated behind an extension the concrete runtime implements, and asking the wrong
/// runtime for it fails loudly instead of silently doing nothing.
/// <c>CommandRouter</c> maps this to the stable <c>RUNTIME_TYPE_MISMATCH</c> error code.
/// </summary>
public sealed class TaruiRuntimeTypeMismatchException(string runtimeName, string expectedExtension)
    : Exception($"The '{runtimeName}' web view runtime does not provide the '{expectedExtension}' extension.")
{
    /// <summary>The runtime that was asked, for example <c>cef</c>.</summary>
    public string RuntimeName { get; } = runtimeName;

    /// <summary>The extension surface that was requested but is not implemented.</summary>
    public string ExpectedExtension { get; } = expectedExtension;

    /// <summary>The stable IPC error code for this failure.</summary>
    public const string ErrorCode = "RUNTIME_TYPE_MISMATCH";
}
