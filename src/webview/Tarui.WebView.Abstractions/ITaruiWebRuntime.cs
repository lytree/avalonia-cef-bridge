namespace Tarui.WebView.Abstractions;

/// <summary>
/// The browser runtime that backs an application's web views. Mirrors Tauri v3's runtime identity
/// (T1/T4): the application selects one runtime and then asks it for the origin, the engine version
/// and the resource mode, instead of assuming a specific browser engine in the shell and hosting
/// layers. Tarui ships a single implementation (<c>CefGlueNextWebRuntime</c>); the abstraction exists
/// so those layers depend on the concept rather than the CEF adapter, leaving a slot for any future
/// engine without introducing runtime reflection or assembly scanning.
/// </summary>
public interface ITaruiWebRuntime
{
    /// <summary>Stable runtime identifier, for example <c>cef</c>.</summary>
    string Name { get; }

    /// <summary>
    /// The underlying browser engine version, for example CEF <c>150.0.11</c>. Mirrors Tauri v3's
    /// <c>RuntimeHandle::webview_version</c>.
    /// </summary>
    string WebviewVersion { get; }

    /// <summary>The application origin, including every scheme the runtime may serve.</summary>
    TaruiAppOrigin Origin { get; }

    /// <summary>
    /// How the runtime serves the application's web assets: <c>http</c> when pages load from an
    /// HTTP(S) origin, <c>scheme</c> when they load from the runtime's custom, portless scheme.
    /// Mirrors the resource-mode decision Tauri v3 moved into the runtime.
    /// </summary>
    string ResourceMode { get; }

    /// <summary>
    /// Ensures the runtime is initialized. Implementations must make this safe to call more than
    /// once; a runtime that is already up either returns immediately or rejects a conflicting
    /// reconfiguration.
    /// </summary>
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases runtime resources. Implementations must make this safe to call more than once.
    /// </summary>
    ValueTask ShutdownAsync();
}
