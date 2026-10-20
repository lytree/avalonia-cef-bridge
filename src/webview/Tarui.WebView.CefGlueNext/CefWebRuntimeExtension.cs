using System.Diagnostics.CodeAnalysis;
using Tarui.WebView.Abstractions;

namespace Tarui.WebView.CefGlueNext;

/// <summary>
/// The CEF-specific runtime surface (Tauri v3's <c>WebviewCefExt</c> / CEF runtime extras). Host code
/// reaches it with
/// <c>runtime.GetRequiredExtension&lt;CefWebRuntimeExtension&gt;()</c>; the abstraction layer and the
/// front end stay engine-neutral, and asking a non-CEF runtime for it reports
/// <c>RUNTIME_TYPE_MISMATCH</c>.
/// <para>
/// The settings are read lazily from <see cref="CefGlueNextAvaloniaRuntime.Options"/>, so the
/// extension reflects what the runtime was actually initialized with (and stays valid before and
/// after initialization) instead of a snapshot taken at composition time.
/// </para>
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "These members implement the instance-based extension contract host code probes for.")]
public sealed class CefWebRuntimeExtension : ITaruiRuntimeExtension
{
    /// <inheritdoc />
    public string RuntimeName => "cef";

    /// <summary>
    /// Mirrors Tauri v3's <c>browser_runtime_style</c>: identifies which browser runtime the platform
    /// surface came from, so host code can branch without probing assembly names.
    /// </summary>
    public string BrowserRuntimeStyle => "cef";

    /// <summary>The CEF runtime version, for example <c>150.0.11</c>.</summary>
    public string EngineVersion => CefGlueNextPortInfo.CefVersion;

    /// <summary>Whether the native runtime has been initialized in this process.</summary>
    public bool IsInitialized => CefGlueNextAvaloniaRuntime.IsInitialized;

    /// <summary>The configured User-Agent override, or null when CEF's default is used.</summary>
    public string? UserAgent => CefGlueNextAvaloniaRuntime.Options?.UserAgent;

    /// <summary>The configured <c>proxy-server</c> switch value, or null when no explicit proxy is set.</summary>
    public string? ProxyServer => CefGlueNextAvaloniaRuntime.Options?.ProxyServer;

    /// <summary>Whether this runtime can serve the DevTools Protocol channel.</summary>
    public bool SupportsDevToolsProtocol => true;
}
