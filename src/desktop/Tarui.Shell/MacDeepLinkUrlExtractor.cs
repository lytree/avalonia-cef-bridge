namespace Tarui.Shell;

/// <summary>
/// Extracts a deep-link URL string from a platform-specific AppleEvent descriptor. The real
/// implementation on macOS reads <c>NSAppleEventDescriptor</c> via Cocoa bindings; the no-op
/// implementation is registered on every other platform so the bridge composition root stays
/// platform-agnostic. The bridge is the sole caller; tests substitute a deterministic fake.
/// </summary>
public interface IMacDeepLinkUrlExtractor
{
    /// <summary>
    /// Returns the deep-link URL carried by the AppleEvent, or <see langword="null"/> when the
    /// descriptor has no URL payload or cannot be safely interpreted as one. The implementation
    /// must not throw on malformed input — it returns <see langword="null"/> so the bridge can
    /// ignore unknown events without breaking the host loop.
    /// </summary>
    string? TryExtractUrl(IntPtr eventDescriptor);
}

/// <summary>
/// Default no-op extractor registered on non-macOS platforms. The pointer is intentionally ignored;
/// the bridge must be a no-op on these platforms because Cocoa bindings are unavailable.
/// </summary>
public sealed class NoOpMacDeepLinkUrlExtractor : IMacDeepLinkUrlExtractor
{
    public string? TryExtractUrl(IntPtr eventDescriptor) => null;
}