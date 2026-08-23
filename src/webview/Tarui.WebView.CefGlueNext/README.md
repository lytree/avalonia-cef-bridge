# Tarui.WebView.CefGlueNext

This project adapts `CefGlue.Next.Avalonia` to the Tarui WebView contracts. It owns Tarui configuration, capability-aware policy/event translation and conversion of component events into `TaruiWebMessage` and other Tarui events.

The project references only `Tarui.WebView.Abstractions`, `Tarui.WebView.Avalonia` and `CefGlue.Next.Avalonia`. It does not reference vendored CefGlue projects or `Xilium.*` directly. CEF runtime initialization, browser controls, native handlers and scheme provider dispatch are owned by `CefGlue.Next.Avalonia`.

## Composition

```text
Tarui.WebView.Abstractions
  navigation, script, download, file-drop and drag-region contracts

Tarui.WebView.Avalonia
  Control-bearing adapter contract for Avalonia hosts

CefGlue.Next.Avalonia
  standalone browser control, CefGlue managed assemblies and runtime lifecycle

Tarui.WebView.CefGlueNext
  Tarui configuration, IPC/policy translation and DI registration
```

The application composition root registers `AddCefGlueWebView()`. A direct Avalonia application that does not use Tarui should consume `CefGlue.Next.Avalonia` instead of this adapter.

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
