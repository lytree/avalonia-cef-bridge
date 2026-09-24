using Xilium.CefGlue;

namespace Xilium.CefGlue.BrowserProcess.Input
{
    /// <summary>
    /// Renderer-side transport for the in-process Blazor Hybrid channel. Installs a
    /// <c>window.__taruiHybridSend(message)</c> global plus a small JS shim that exposes the
    /// <c>window.external.sendMessage / receiveMessage</c> surface consumed by
    /// <c>blazor.webview.js</c> (Microsoft.AspNetCore.Components.WebView). When the shim's
    /// <c>sendMessage</c> is invoked, the message string is posted to the browser process as a
    /// "__taruiHybrid" process message; the browser process raises
    /// <see cref="Common.CommonBrowserAdapter.HybridWebMessageReceived"/>.
    /// Host-to-JS delivery does not use process messages: the host executes
    /// <c>window.__taruiHybridReceive(base64)</c>, and the shim base64-decodes the payload and
    /// invokes the callback registered through <c>window.external.receiveMessage</c>.
    /// </summary>
    internal sealed class HybridChannelRenderSide : CefV8Handler
    {
        public const string FunctionName = "__taruiHybridSend";
        public const string ReceiveFunctionName = "__taruiHybridReceive";
        public const string MessageName = "__taruiHybrid";

        /// <summary>
        /// The shim maps blazor.webview.js's WebView2/Electron-style <c>window.external</c> API onto
        /// the Tarui hybrid channel. Messages are plain strings (the webview IPC protocol prefixes
        /// them with "__bwv:" internally); base64 is only used on the host-to-JS direction to avoid
        /// string-escaping issues inside ExecuteJavaScript.
        /// </summary>
        private const string ShimScript = """
            (function () {
                'use strict';
                if (window.__taruiHybridInstalled) { return; }
                window.__taruiHybridInstalled = true;
                var external = window.external;
                if (!external || typeof external !== 'object') {
                    try { external = {}; window.external = external; } catch (e) { return; }
                }
                try {
                    external.sendMessage = function (message) {
                        if (typeof message === 'string') { window.__taruiHybridSend(message); }
                    };
                    external.receiveMessage = function (callback) {
                        window.__taruiHybridCallback = callback;
                    };
                } catch (e) { return; }
                window.__taruiHybridReceive = function (base64) {
                    var callback = window.__taruiHybridCallback;
                    if (typeof callback !== 'function' || typeof base64 !== 'string') { return; }
                    callback(atob(base64));
                };
            })();
            """;

        /// <summary>Install the global function and the window.external shim into a (main-frame) V8 context.</summary>
        public void Install(CefV8Context context)
        {
            if (!context.Enter()) return;
            try
            {
                var global = context.GetGlobal();
                global.SetValue(FunctionName, CefV8Value.CreateFunction(FunctionName, this));
                if (context.TryEval(ShimScript, "tarui://hybrid-shim.js", 1, out _, out _))
                {
                    // The shim is optional glue for Blazor Hybrid hosting; a failure to evaluate it
                    // (for example on pages with hostile CSP) must not break the regular IPC channel.
                }
            }
            finally
            {
                context.Exit();
            }
        }

        protected override bool Execute(string name, CefV8Value obj, CefV8Value[] arguments,
            out CefV8Value returnValue, out string exception)
        {
            returnValue = null;
            exception = null;

            if (arguments.Length >= 1)
            {
                var message = arguments[0].GetStringValue();
                var frame = CefV8Context.GetCurrentContext()?.GetFrame();
                if (frame != null && message != null)
                {
                    using var processMessage = CefProcessMessage.Create(MessageName);
                    using (var args = processMessage.Arguments)
                    {
                        args.SetString(0, message);
                    }
                    frame.SendProcessMessage(CefProcessId.Browser, processMessage);
                }
            }
            return true;
        }
    }
}
