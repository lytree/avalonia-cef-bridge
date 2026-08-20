# tarui.net

`tarui.net` is a desktop application skeleton that combines an Avalonia native shell with a React/TypeScript business UI.

## Architecture

- Avalonia is the native window shell and component layer.
- `Tarui.WebView.Native` is the working NativeWebView backend; `Tarui.WebView.CefGlueNext` is the browser source-port boundary, and the Shell only sees `Tarui.WebView.Abstractions`.
- IPC follows the Tauri shape: `Command`, `Event`, `Channel`, and `Capability`.
- Runtime reflection, assembly scanning, `Activator`, `dynamic`, `DynamicInvoke`, dynamic plugin loading, and JSON reflection fallback are prohibited in application code.
- Discovery and binding use source generation; JSON uses `System.Text.Json` source generation.
- Plugins are referenced by project and registered explicitly in the composition root.

## Projects

```text
src/Tarui.Contracts       Shared IPC contracts and generated JSON metadata
src/Tarui.Ipc             Static command router, events, channels, capabilities
src/Tarui.Ipc.Generators  Roslyn incremental generator for command catalogs
src/Tarui.Plugins.Core    Explicit core plugin registration
src/Tarui.Plugins.Dialog  Explicit dialog plugin registration
src/Tarui.WebView.Abstractions  Engine-neutral WebView contract
src/Tarui.WebView.Native        Working NativeWebView backend
src/Tarui.WebView.CefGlueNext   Pinned CefGlue source adapter boundary
src/Tarui.Shell                 Avalonia.Markup.Declarative shell, depends only on WebView abstractions
src/Tarui.App                   Composition root and explicit backend selection
src/Tarui.Web             React/TypeScript business UI and @tarui/api bridge
tests/Tarui.Ipc.Tests     Contract and router tests
```

## Build

```powershell
dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln --no-restore
dotnet run --project tests/Tarui.Ipc.Tests/Tarui.Ipc.Tests.csproj --no-restore
cd src/Tarui.Web
pnpm install
pnpm build
```

The repository-level `NuGet.Config` intentionally clears the machine's invalid local Avalonia source without changing user-wide configuration.

## WebView backend

`Tarui.App` explicitly selects `NativeWebViewFactory` by default. Set `TARUI_WEBVIEW_BACKEND=cefglue` to exercise `CefGlueNextWebViewFactory`. The CefGlue implementation is a compiling stub because the pinned upstream source targets Avalonia 11.3.14 while tarui.net targets Avalonia 12.1.1.

When the port is ready, set `EnableCefGlueNextSourcePort=true` and provide `CefGlueNextReviewedSourceFiles` as an explicit semicolon-separated file list. Do not use wildcard `Compile` items. Upstream ObjectBinding and serializer code uses reflection and must not be compiled into tarui.net; IPC continues to use its own static source-generated binding and JSON chain. Do not add runtime redists, demo projects, or the upstream solution.
