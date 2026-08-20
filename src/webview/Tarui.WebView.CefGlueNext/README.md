# Tarui.WebView.CefGlueNext

This project adapts the vendored CefGlue source to `ITaruiWebView`. It owns runtime initialization, same-executable subprocess startup, navigation, script execution, resource transport, and conversion of CEF process messages into `TaruiWebMessage`.

The implementation has no CefGlue NuGet dependency. Managed CefGlue projects live in `../cefglue`; native CEF assets are installed through `eng/cef/install-runtime.ps1`.

## Resource modes

- `http`: loads `TARUI_WEB_URL`, suitable for Vite or a local HTTP service.
- `scheme`: registers `tarui://localhost` and serves `TARUI_WEB_ROOT` or packaged `web` output directly, without an HTTP server.

Scheme mode uses exact origin validation, main-frame-only SPA fallback, MIME mapping, CSP, size limits and traversal/reparse-point checks. The renderer exposes only the fixed `window.invokeCSharpAction` bridge; upstream reflection-based JavaScript object binding is not included.
