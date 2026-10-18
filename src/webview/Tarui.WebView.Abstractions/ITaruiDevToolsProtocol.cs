namespace Tarui.WebView.Abstractions;

/// <summary>
/// Raised for a Chrome DevTools Protocol message emitted by the browser — a CDP event or a method
/// result. <see cref="Message"/> is the raw JSON CDP payload.
/// </summary>
public sealed class TaruiDevToolsMessageEventArgs : EventArgs
{
    public TaruiDevToolsMessageEventArgs(string message)
    {
        Message = message;
    }

    /// <summary>The raw CDP JSON message.</summary>
    public string Message { get; }
}

/// <summary>
/// An optional, engine-neutral DevTools Protocol channel: a browser backend that supports CDP
/// implements it, and the host probes for it with <c>is</c> (absence is reported as
/// <c>NOT_SUPPORTED</c>). It is deliberately separate from <see cref="ITaruiWebView"/> so a backend
/// without a CDP surface is not forced to implement one; the CEF surface stays isolated to
/// <c>Tarui.WebView.CefGlueNext</c>, mirroring Tauri v3's runtime-specific extension pattern.
/// </summary>
public interface ITaruiDevToolsProtocol
{
    /// <summary>
    /// Sends a raw CDP JSON message to the browser. Returns <c>false</c> when the browser is not ready
    /// to receive messages (not initialized) instead of throwing.
    /// </summary>
    bool TrySendDevToolsMessage(string message);

    /// <summary>Raised for every CDP message the browser emits that no extension consumed.</summary>
    event EventHandler<TaruiDevToolsMessageEventArgs>? DevToolsMessageReceived;
}
