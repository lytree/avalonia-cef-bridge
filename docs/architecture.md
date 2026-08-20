# tarui.net Architecture

## Runtime boundary

Avalonia owns the window, title bar, native dialogs, platform services, WebView lifecycle, and recovery UI. The WebView owns application routes, forms, tables, and business state.

The shell depends on `Tarui.WebView.Abstractions`, never on a concrete browser control. `Tarui.App` is the composition root and explicitly selects `NativeWebViewFactory` by default; `CefGlueNextWebViewFactory` is available behind `TARUI_WEBVIEW_BACKEND=cefglue`. The current CefGlueNext adapter is a compiling placeholder until the pinned Avalonia 11.3.14 source is ported to the Avalonia 12.1.1 API.

The shell creates all services and calls each plugin's `Register` method explicitly. There is no plugin directory, assembly scan, runtime type lookup, or reflection-based dependency injection.

## IPC

The browser side sends a Tauri-shaped invoke envelope through the selected WebView adapter. The shell routes the command through a `FrozenDictionary<string, ICommandInvoker>`, checks the current capability, deserializes through `TaruiJsonContext`, and calls a strongly typed invoker.

- Command: request/response work.
- Event: low-frequency notifications.
- Channel: ordered progress messages owned by a command.
- Capability: allow-list of commands for a window/webview.

## Code generation

`Tarui.Ipc.Generators` is an incremental generator. It reads `[TaruiCommand]` declarations at compile time and emits a static catalog. `Tarui.Contracts` uses `JsonSerializerContext` for all wire DTOs. Runtime code never scans assemblies or invokes a method through metadata.

## WebView backend boundary

`Tarui.WebView.Native` is the runnable default and wraps Avalonia's `NativeWebView`. `Tarui.WebView.CefGlueNext` is the only project allowed to know about CefGlue implementation details. The pinned source lives under `third_party/CefGlue`. Enabling the source port requires `EnableCefGlueNextSourcePort=true` plus an explicit `CefGlueNextReviewedSourceFiles` file list; wildcard compilation is forbidden. Upstream ObjectBinding and serializer code uses reflection and must not be compiled into tarui.net. IPC continues to use its own static source-generated binding and JSON chain.

## Web resource lifecycle

Development loads the Vite server URL. Production can point `WebViewHost` at a packaged local origin. The current skeleton keeps the source configurable through `TARUI_WEB_URL`; NativeWebView is the runnable path and CefGlueNext is the explicit source-port review path.
