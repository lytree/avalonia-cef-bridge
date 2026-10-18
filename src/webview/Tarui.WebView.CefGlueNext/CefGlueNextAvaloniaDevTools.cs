using System.Runtime.InteropServices;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace Tarui.WebView.CefGlueNext;

/// <summary>
/// Captures the created <see cref="CefBrowser"/> so the dev tools protocol channel can attach to its
/// host. The bundled fork's <c>CommonCefLifeSpanHandler</c> chains to this handler before running its
/// own browser-created logic, so popup and lifecycle behaviour is unchanged.
/// </summary>
internal sealed class CefGlueNextAvaloniaDevToolsLifeSpanHandler(CefGlueNextAvaloniaWebView owner)
    : LifeSpanHandler
{
    protected override void OnAfterCreated(CefBrowser browser) => owner.AttachDevTools(browser);
}

/// <summary>
/// Receives CDP messages (events and method results) from the browser and forwards the raw JSON to the
/// owning web view. The native <c>message</c> buffer is only valid for the scope of the callback, so it
/// is copied eagerly. Returning <c>true</c> consumes the message so it is not re-dispatched to the
/// specialized callbacks — the raw JSON already carries both method results and events.
/// </summary>
internal sealed class CefGlueNextAvaloniaDevToolsObserver(CefGlueNextAvaloniaWebView owner)
    : CefDevToolsMessageObserver
{
    protected override bool OnDevToolsMessage(CefBrowser browser, IntPtr message, int messageSize)
    {
        if (messageSize > 0)
        {
            var json = Marshal.PtrToStringUTF8(message, messageSize);
            if (!string.IsNullOrEmpty(json))
            {
                owner.RaiseDevToolsMessage(json);
            }
        }

        return true;
    }

    protected override void OnDevToolsMethodResult(
        CefBrowser browser,
        int messageId,
        bool success,
        IntPtr result,
        int resultSize)
    {
    }

    protected override void OnDevToolsEvent(CefBrowser browser, string method, IntPtr parameters, int parametersSize)
    {
    }

    protected override void OnDevToolsAgentAttached(CefBrowser browser)
    {
    }

    protected override void OnDevToolsAgentDetached(CefBrowser browser)
    {
    }
}
