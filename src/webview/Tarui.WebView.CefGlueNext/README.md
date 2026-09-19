# Tarui.WebView.CefGlueNext

This project is Tarui's sole entry point to the vendored CEF runtime: it owns the CefGlue composition (runtime loader, browser control, native handlers, scheme providers, cookie store) and adapts them to the Tarui WebView contracts. It owns Tarui configuration, capability-aware policy/event translation and conversion of component events into `TaruiWebMessage` and other Tarui events.

The project references `Tarui.WebView.Abstractions`, `Tarui.WebView.Avalonia`, and the vendored `CefGlue.Avalonia` / `CefGlue.BrowserProcess.Core` / `CefGlue.Common` projects that live under `src/webview/cefglue/`. The browser control, scheme handlers, and CEF subprocess lifecycle are now part of this assembly rather than a sibling `CefGlue.Next.Avalonia` package.

## Composition

```text
Tarui.WebView.Abstractions
  navigation, script, download, file-drop and drag-region contracts

Tarui.WebView.Avalonia
  Control-bearing adapter contract for Avalonia hosts

Tarui.WebView.CefGlueNext
  CEF runtime + browser control + Tarui configuration + IPC/policy translation
```

The application composition root registers `AddCefGlueWebView()`. Direct Avalonia applications that need only the CEF control can take a `ProjectReference` to this project; vendored `Xilium.*` types remain internal to it.

## Lifecycle

The composition root dispatches subprocess arguments first, starts the host, and creates windows after runtime initialization. Shutdown is ordered as follows:

```text
CefGlueNextAvaloniaRuntime.RunSubProcess(args)
  -> host/application startup
  -> CefGlueNextAvaloniaRuntime.Initialize(...)
  -> create Tarui WebViews
  -> close windows and await each WebView CloseAsync/DisposeAsync
  -> Avalonia loop exits
  -> Host StopAsync and Dispose complete
  -> Program finally calls CefGlueNextAvaloniaRuntime.Shutdown()
```

`Tarui.WebView.CefGlueNext` translates component decisions into Tarui events and policies. It must remain unaware of vendored CefGlue implementation types.

## Resource modes

- `http`: loads `TARUI_WEB_URL`, suitable for Vite or a local HTTP service.
- `scheme`: registers `tarui://localhost` and serves `TARUI_WEB_ROOT` or packaged `web` output directly, without an HTTP server.

The two schemes can coexist: when a content root is configured (config key, `TARUI_WEB_ROOT`, or packaged assets), HTTP mode also registers the portless custom scheme, so a single application can load remote HTTP content and local assets side by side. `CefGlueNextWebAppOptions.AllowedSchemes` lists every accepted scheme and `SchemeOrigin` exposes the custom-scheme origin (`null` without local assets); CEF registers the scheme handler whenever `ContentRoot` exists, regardless of mode.

Scheme mode uses exact origin validation, main-frame-only SPA fallback, MIME mapping, CSP, size limits and traversal/reparse-point checks. The renderer exposes only the fixed `window.invokeCSharpAction` bridge; upstream reflection-based JavaScript object binding is not included.
