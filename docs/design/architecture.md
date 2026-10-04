# Architecture

> 仓库内的所有权边界、分层依赖、模块生命周期。

## Ownership boundaries

Avalonia owns the window, native dialogs, platform services, WebView presentation lifecycle, and recovery UI. The Web application owns routes, forms, tables, and business state. The browser implementation is a replaceable component boundary rather than a Shell concern.

The Shell depends on `Tarui.WebView.Abstractions` and the Avalonia-only `Tarui.WebView.Avalonia` hosting contract; it never references Xilium CefGlue. `examples/demo` (the `Demo` app) registers `Tarui.WebView.CefGlueNext` through `AddCefGlueWebView()`, and that adapter is the sole CefGlue component entry (the legacy `CefGlue.Next.Avalonia` package has been absorbed into `Tarui.WebView.CefGlueNext`). Plugins are referenced and registered explicitly at the composition root through `AddPlugin<T>()` / `Add*Plugin()`; there is no plugin scan, runtime type lookup, or reflection-based dependency injection.

## Browser package graph

```text
Tarui.WebView.Abstractions
  UI-neutral WebView contracts
       |
Tarui.WebView.Avalonia
  Avalonia Control hosting contract
       |
Tarui.Shell ----------------------+
                                  |
Tarui.WebView.CefGlueNext --------+--> CefGlueNext component layer
                                       (Avalonia control + CefGlue handlers
                                       + runtime/subprocess lifecycle)
```

`Tarui.WebView.Abstractions` must not reference Avalonia or Xilium assemblies. `Tarui.Shell` and `Tarui.Hosting` may use Avalonia for native UI, but must not reference Xilium CefGlue. The vendored projects under `src/webview/cefglue` are implementation inputs consumed only by `Tarui.WebView.CefGlueNext`; they are not application-facing package dependencies.

## Project layers

| 项目 | 关键类型 | 职责 |
| --- | --- | --- |
| `Tarui.Contracts` | DTO record、`TaruiJsonContext`(`JsonSerializerContext`) | 跨进程序列化契约,零运行时依赖 |
| `Tarui.Ipc` | `ITaruiPlugin`、`AddPlugin<T>()`、`CommandRouterBuilder`、`IpcDispatcher` | 插件抽象、命令路由器、权限登记(`RegisteredPermissions`) |
| `Tarui.Shell` | `AddTaruiShell`、`WindowRegistry`、`EventRouter`、`CapabilitySetProvider`、`ShellWindowFactory`、`MainWindowLauncher`、`IpcDispatcher` 接入 | 声明式组合,所有插件均 `ProjectReference` 引入 |
| `Tarui.Hosting` | `TaruiHost.CreateApplicationBuilder`、`TaruiApplicationBuilder`、`TaruiApplication`、`TaruiAvaloniaApp`、`TaruiLifetimeBridge`、`HostShutdownWatcher` | 注入 M.E.Hosting、Avalonia lifecycle 桥接 |
| `Tarui.SingleInstance` | `SingleInstanceGuard`、`SingleInstanceIdentity`、`InstanceRole` | 二次启动参数转发到主进程 |
| `Tarui.WebView.Abstractions` | `IWebViewHost`、`INavigationRequest`、`IDownloadRequest` | UI 中立契约,无 Avalonia/CefGlue |
| `Tarui.WebView.Avalonia` | `TaruiWebView`(Avalonia Control) | Control 承载层 |
| `Tarui.WebView.CefGlueNext` | `AddCefGlueWebView()`、`CefGlueNextWebAppOptions`、`CefGlueNextAvaloniaWebView`、`CefGlueNextAvaloniaRuntime` | Tarui 事件/策略/Capability 适配 + 浏览器组件实现,nupkg 内嵌 Xilium CefGlue DLL(原 `CefGlue.Next.Avalonia` 已并入) |
| `Tarui.Ipc.Generators` | `IIncrementalGenerator` | 源生成 TaruiJsonContext 与强类型 invoker |

依赖方向(强约束,被架构门禁检查):

```text
Hosting  →  Shell  →  (Ipc, Contracts, WebView.Abstractions, WebView.Avalonia, 插件接口)
                          ↑
              CefGlueNext  →  (WebView.Abstractions, WebView.Avalonia)
                          ↑
                  Tarui.WebView.CefGlueNext(包内嵌 5 个 Xilium DLL)
```

`Hosting` 和 `Shell` 都不引用 Xilium CefGlue 程序集;`Tarui.WebView.CefGlueNext` 是唯一接触 CefGlue 实现类型的项目;`webview/cefglue/*` 只能被 `Tarui.WebView.CefGlueNext` 引用。

## Hosting

`Tarui.Hosting` owns the host layer: `TaruiHost.CreateApplicationBuilder()` returns a `TaruiApplicationBuilder` (`Configuration`, `Logging`, `Services`, `Window`) built on `Microsoft.Extensions.Hosting`. The content root is fixed to `AppContext.BaseDirectory`, so `appsettings.json` and the copied `capabilities/*.json` resolve from the application output. `TaruiApplication.Run()` starts the host, uses the Avalonia classic desktop lifetime as the blocking run loop, and stops and disposes the host on exit. `IHostApplicationLifetime.StopApplication()` (including the Ctrl+C console-lifetime semantics) closes the UI through `TaruiLifetimeBridge` and `HostShutdownWatcher`; closing the window lets Avalonia exit and stops the host cooperatively.

`examples/demo` (the `Demo` app) is the composition root: it registers the shell and plugins explicitly through `AddTaruiShell()` and the `Add*Plugin()` extensions, and configures the main window through `builder.Window`, merged over the `Tarui:Window:*` configuration keys (defaults < configuration < code). `tests/Tarui.Hosting.Tests` covers the builder, configuration merging, and the lifetime bridge. See [`hosting.md`](hosting.md) for the full design and the configuration key table.

## Managed browser stack

The browser stack is compiled from projects under `src/webview/cefglue` and published through `Tarui.WebView.CefGlueNext`:

- `CefGlue.Core`: generated CEF P/Invoke bindings and native API wrappers.
- `CefGlue.Common.Shared`: process messages, pipes, and generated JSON metadata.
- `CefGlue.Common`: browser lifecycle and windowed hosting.
- `CefGlue.BrowserProcess.Core`: same-executable CEF subprocess entry and renderer bridge.
- `CefGlue.Avalonia`: Avalonia 12 native control host, embedded in the component package.

The `Tarui.WebView.CefGlueNext` nupkg contains `Tarui.WebView.CefGlueNext.dll` plus all five required `Xilium.CefGlue*.dll` assemblies. Its nuspec declares Avalonia but no Xilium/CefGlue package dependency. No other Tarui project may reference the vendored CefGlue projects directly.

## Shell composition

`Demo.Program` composes the application on `TaruiHost.CreateApplicationBuilder`. `AddTaruiShell()` registers the shell services and each `Add*Plugin()` extension registers one plugin through `AddPlugin<T>()` — explicit, compile-time registration with no plugin scan, runtime type lookup, or reflection-based dependency injection.

`AddTaruiShell()` builds, in order:

1. `WindowRegistry` — label-to-entry map for live windows.
2. `EventRouter` — fan-out of routed (window-targeted) and broadcast events over `EventHub`. Web-originated events are confined to the reserved `user://` namespace; reserved native prefixes (`app://`, `window://`, `shell://`, ...) are unreachable from the renderer.
3. `ICapabilityProvider` (`CapabilitySetProvider`) — reads `capabilities/*.json`; each window resolves its own explicitly declared capability profile, and a window without one is rejected (`CAPABILITY_NOT_FOUND`) rather than inheriting `main`.
4. `CommandRouter` — composed by `CommandRouterComposer` from every registered `ITaruiPlugin`: each plugin's `ConfigureCommands(CommandRouterBuilder)` adds its commands and permissions, then capability validation fails startup when a capability references a permission no plugin registered.
5. `IpcDispatcher` — wraps the frozen command router; every `WebViewHost` dispatches with the `CommandContext` of its own window, so the shell-side label is authoritative even when the Web envelope carries a stale one.
6. Window services — `ShellWindowFactory`, `AvaloniaWindowService`, `AvaloniaDialogService`, `AvaloniaClipboardService`, and `MainWindowLauncher` for the `main` window entry and lifecycle wiring.

`AvaloniaWindowService` implements the 24 `core:window|*` commands over `WindowRegistry` and `ShellWindow`, including monitor discovery. `AvaloniaDialogService` and `AvaloniaClipboardService` resolve the owner window from the registry so dialogs and clipboard access stay attached to the requesting window.

Window lifecycle events are wired per entry: `window://moved`, `window://resized`, `window://focus-changed`, and `window://close-requested` are routed to the owning window's Webview; `window://destroyed` and `shell://theme-changed` broadcast to all windows. Closing is cooperative — the OS close request is cancelled and surfaced as `window://close-requested`; only `core:window|close` (which sets the entry's close-pending flag) actually destroys the window. `core:window|deny-close` is the Web-side receipt that cancels the force-close fallback timer (front-end never confirms). The timeout is configurable via `Tarui:Window:CloseRequestTimeout` (seconds; `0` requires explicit `core:window|close force=true`).

Reserved native events are delivered to a window only when its capability `events` list authorizes receiving them (`capabilities/*.json` declares `window://*` and `shell://theme-changed` for the demo windows); `user://` events carry no native data and reach any window. This prevents second-instance arguments, file paths, and notification actions from leaking to unauthorized windows.

## Frontend bridge

`@lytree/api` mirrors the plugin contracts as typed TypeScript modules (`ipc`, `app`, `window`, `event`, `dialog`, `os`, `path`, `process`, `shell`, `clipboard`, ...). The `Window` class addresses the current Webview's window when label-less and a specific window via `getByLabel`; lifecycle subscriptions (`onMoved`, `onResized`, `onFocusChanged`, `onCloseRequested`, `onDestroyed`, `denyClose`) wrap the shared `listen` registry. Responses resolve through the base64 dispatch channel installed by `WebViewHost`; failures reject with `IpcCommandError` carrying the router's error code.

## Process model

`Demo.Program` calls `CefGlueNextAvaloniaRuntime.RunSubProcess(args)` before the host builder is created. CEF renderer and utility process launches therefore reuse the same executable, while the normal browser process continues into the host and Avalonia. `Tarui.WebView.CefGlueNext` keeps a compatibility forwarding method for Tarui callers, but the component runtime is the lifecycle owner.

## Native runtime

CEF native binaries are installed with `eng/cef/install-runtime.ps1` into `runtime/cef/<rid>`. They are downloaded from the official CEF automated build endpoint, checksum verified, and copied into application output when present. This keeps large binaries out of normal Git history without introducing a NuGet runtime dependency.

The managed component and native runtime have separate distribution responsibilities: `Tarui.WebView.CefGlueNext` carries managed CefGlue assemblies, while the application supplies the matching native CEF distribution. A future RID runtime package can replace the repository installer without changing the Avalonia component API.

## Rendering scope

The Avalonia 12 port currently supports native windowed rendering. OSR, shared-frame delivery UI, and Avalonia 11 drag-and-drop adapters are excluded from the Avalonia project until a dedicated Avalonia 12 implementation is required.

## Lifecycle order

The process-level order is intentionally explicit:

```text
CefGlueNextAvaloniaRuntime.RunSubProcess(args)
  -> create Host and Avalonia lifetime
  -> initialize CefGlue runtime once
  -> create windows and WebViews
  -> close windows
  -> await every WebView CloseAsync/DisposeAsync and BrowserClosed callback
  -> Avalonia loop exits
  -> Host StopAsync and Dispose complete
  -> Program finally calls CefGlueNextAvaloniaRuntime.Shutdown()
```

The runtime must not be shut down while a browser control is still waiting for its native close callback. Avalonia and the Host must finish their stop/dispose sequence before the composition root's `finally` block shuts down CEF. This ordering is part of the component contract and is checked by the application composition and release documentation.
