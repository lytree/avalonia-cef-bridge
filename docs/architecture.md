# tarui.net Architecture

## Ownership boundaries

Avalonia owns the window, native dialogs, platform services, WebView lifecycle, and recovery UI. The Web application owns routes, forms, tables, and business state.

The Shell depends only on `Tarui.WebView.Abstractions`. `Tarui.App` registers the CefGlue WebView through `AddCefGlueWebView()`. Plugins are referenced and registered explicitly at the composition root through `AddPlugin<T>()` / `Add*Plugin()`; there is no plugin scan, runtime type lookup, or reflection-based dependency injection.

## Hosting

`Tarui.Hosting` owns the host layer: `TaruiHost.CreateApplicationBuilder()` returns a `TaruiApplicationBuilder` (`Configuration`, `Logging`, `Services`, `Window`) built on `Microsoft.Extensions.Hosting`. The content root is fixed to `AppContext.BaseDirectory`, so `appsettings.json` and the copied `capabilities/*.json` resolve from the application output. `TaruiApplication.Run()` starts the host, uses the Avalonia classic desktop lifetime as the blocking run loop, and stops and disposes the host on exit. `IHostApplicationLifetime.StopApplication()` (including the Ctrl+C console-lifetime semantics) closes the UI through `TaruiLifetimeBridge` and `HostShutdownWatcher`; closing the window lets Avalonia exit and stops the host cooperatively.

`Tarui.App` is the composition root: it registers the shell and plugins explicitly through `AddTaruiShell()` and the `Add*Plugin()` extensions, and configures the main window through `builder.Window`, merged over the `Tarui:Window:*` configuration keys (defaults < configuration < code). `tests/Tarui.Hosting.Tests` covers the builder, configuration merging, and the lifetime bridge. See `docs/hosting.md` for the full design and the configuration key table.

## Managed browser stack

The browser stack is compiled entirely from projects under `src/webview/cefglue`:

- `CefGlue.Core`: generated CEF P/Invoke bindings and native API wrappers.
- `CefGlue.Common.Shared`: process messages, pipes, and generated JSON metadata.
- `CefGlue.Common`: browser lifecycle and windowed hosting.
- `CefGlue.BrowserProcess.Core`: same-executable CEF subprocess entry and renderer bridge.
- `CefGlue.Avalonia`: Avalonia 12 native control host.

No CefGlue, ReactiveUI, System.Reactive, Avalonia WebView, or CEF runtime NuGet package is referenced. The remaining Avalonia package references provide the application framework itself.

## IPC

The renderer injects `window.invokeCSharpAction(json)`. Calls become a fixed `__taruiIpc` CEF process message, are raised by the adapter as `TaruiWebMessage`, then flow through the DI-composed `CommandRouter`. Host responses use encoded JavaScript dispatch through the existing WebView abstraction.

- Command: request/response work.
- Event: low-frequency notifications.
- Channel: ordered progress messages owned by a command.
- Capability: command allow-list for a window or WebView.

The upstream reflection-based ObjectBinding and generic JavaScript serializer were removed from the vendored source. Wire DTOs use `JsonSerializerContext`; commands use explicit or generated strongly typed invokers.

## Shell composition

`Tarui.App.Program` composes the application on `TaruiHost.CreateApplicationBuilder`. `AddTaruiShell()` registers the shell services and each `Add*Plugin()` extension registers one plugin through `AddPlugin<T>()` — explicit, compile-time registration with no plugin scan, runtime type lookup, or reflection-based dependency injection.

`AddTaruiShell()` builds, in order:

1. `WindowRegistry` — label-to-entry map for live windows.
2. `EventRouter` — fan-out of routed (window-targeted) and broadcast events over `EventHub`. Web-originated events are confined to the reserved `user://` namespace; reserved native prefixes (`app://`, `window://`, `shell://`, ...) are unreachable from the renderer.
3. `ICapabilityProvider` (`CapabilitySetProvider`) — reads `capabilities/*.json`; each window resolves its own explicitly declared capability profile, and a window without one is rejected (`CAPABILITY_NOT_FOUND`) rather than inheriting `main`.
4. `CommandRouter` — composed by `CommandRouterComposer` from every registered `ITaruiPlugin`: each plugin's `ConfigureCommands(CommandRouterBuilder)` adds its commands and permissions, then capability validation fails startup when a capability references a permission no plugin registered.
5. `IpcDispatcher` — wraps the frozen command router; every `WebViewHost` dispatches with the `CommandContext` of its own window, so the shell-side label is authoritative even when the Web envelope carries a stale one.
6. Window services — `ShellWindowFactory`, `AvaloniaWindowService`, `AvaloniaDialogService`, `AvaloniaClipboardService`, and `MainWindowLauncher` for the `main` window entry and lifecycle wiring.

`AvaloniaWindowService` implements the 24 `core:window|*` commands over `WindowRegistry` and `ShellWindow`, including monitor discovery. `AvaloniaDialogService` and `AvaloniaClipboardService` resolve the owner window from the registry so dialogs and clipboard access stay attached to the requesting window.

Window lifecycle events are wired per entry: `window://moved`, `window://resized`, `window://focus-changed`, and `window://close-requested` are routed to the owning window's Webview; `window://destroyed` and `shell://theme-changed` broadcast to all windows. Closing is cooperative — the OS close request is cancelled and surfaced as `window://close-requested`; only `core:window|close` (which sets the entry's close-pending flag) actually destroys the window.

Reserved native events are delivered to a window only when its capability `events` list authorizes receiving them (`capabilities/*.json` declares `window://*` and `shell://theme-changed` for the demo windows); `user://` events carry no native data and reach any window. This prevents second-instance arguments, file paths, and notification actions from leaking to unauthorized windows.

## Plugin command surface

| Plugin | Commands |
| --- | --- |
| Core | `core:app|get-info` |
| Window | `core:window|create/close/minimize/maximize/unmaximize/toggle-maximize/hide/show/focus/center/set-title/set-size/set-position/set-min-size/set-max-size/set-always-on-top/set-resizable/set-decorations/set-fullscreen/get-state/current-monitor/primary-monitor/monitors/list` |
| Event | `core:event|emit` |
| Dialog | `plugin:dialog|open`, `plugin:dialog|save` |
| System | `core:path|resolve`, `core:os|info`, `core:process|exit`, `core:process|relaunch`, `core:shell|open`, `core:clipboard|read-text`, `core:clipboard|write-text` |

Adding a command means: DTO record in `Tarui.Contracts` (plus `TaruiJsonContext` registration), a handler wired in the plugin class's `ConfigureCommands(CommandRouterBuilder)`, a `commands.Add` entry with its permission, and the permission listed in the target capability file.

## Frontend bridge

`@tarui/api` mirrors the plugin contracts as typed TypeScript modules (`ipc`, `app`, `window`, `event`, `dialog`, `os`, `path`, `process`, `shell`, `clipboard`). The `Window` class addresses the current Webview's window when label-less and a specific window via `getByLabel`; lifecycle subscriptions (`onMoved`, `onResized`, `onFocusChanged`, `onCloseRequested`, `onDestroyed`) wrap the shared `listen` registry. Responses resolve through the base64 dispatch channel installed by `WebViewHost`; failures reject with `IpcCommandError` carrying the router's error code.

## Process model

`Tarui.App.Program` calls `CefGlueRuntimeBootstrap.RunSubProcess(args)` before the host builder is created. CEF renderer and utility process launches therefore reuse the same executable, while the normal browser process continues into the host and Avalonia.

## Native runtime

CEF native binaries are installed with `eng/cef/install-runtime.ps1` into `runtime/cef/<rid>`. They are downloaded from the official CEF automated build endpoint, checksum verified, and copied into application output when present. This keeps large binaries out of normal Git history without introducing a NuGet runtime dependency.

## Web resource transport

`CefGlueNextWebAppOptions` (built from the `Tarui:Web:*` configuration keys, with `TARUI_WEB_*` environment variables as fallback) selects one of two explicit modes before CEF initialization:

- HTTP: navigate to an exact `http://` or `https://` origin, primarily for Vite development or a managed local server.
- Scheme: register `tarui://localhost` in browser and renderer processes and serve packaged files directly through `CefSchemeHandlerFactory`. No HTTP listener is created.

Scheme requests accept GET and HEAD only. Resolution validates the exact origin, rejects userinfo, ports, traversal encodings, control characters, colon/device paths and reparse points, applies a file-size limit, sends strict MIME types and CSP, and enables SPA fallback only for missing extensionless main-frame navigation. Static resource misses remain 404. Registration failures terminate startup.

## Rendering scope

The Avalonia 12 port currently supports native windowed rendering. OSR, shared-frame delivery UI, and Avalonia 11 drag-and-drop adapters are excluded from the Avalonia project until a dedicated Avalonia 12 implementation is required.
