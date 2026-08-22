# tarui.net

`tarui.net` combines an Avalonia native shell with a React/TypeScript business UI and an in-repository CefGlue browser backend.

## Architecture

- Avalonia owns the native window shell and platform components.
- CefGlue.Next managed sources are vendored under `src/webview/cefglue` and adapted to Avalonia 12.1.1.
- IPC follows the Tauri shape: Command, Event, Channel, and Capability.
- Runtime reflection, assembly scanning, dynamic plugin loading, and JSON reflection fallback are prohibited.
- Hosting follows the ASP.NET Core pattern: `TaruiHost.CreateApplicationBuilder` composes the shell through `AddTaruiShell()` / `Add*Plugin()` DI extensions on top of `Microsoft.Extensions.Hosting`.
- Plugins are project references registered explicitly at the composition root via `AddPlugin<T>()` / `Add*Plugin()` — compile-time registration, no assembly scanning.

Desktop capability expansion and Tauri v2 alignment are tracked in
[`docs/tauri-desktop-alignment-plan.md`](docs/tauri-desktop-alignment-plan.md).

## Repository layout

```text
src/
  core/                    Reflection-free contracts and IPC runtime
  desktop/
    Tarui.Hosting/          ASP.NET Core style host: builder, DI, configuration, logging, host lifetime
    Tarui.Shell/            Declarative shell and window composition
    Tarui.App/              Application composition root
  generators/              Compile-time Roslyn generators
  plugins/                 Explicit native capability modules
  webview/
    cefglue/                Vendored CefGlue managed source projects
    Tarui.WebView.*         Tarui browser abstraction and adapter
web/
  apps/Tarui.Web/          React business application
  packages/api/            @tarui/api bridge package
tests/                     Executable and integration tests
capabilities/              Window/WebView permission manifests
runtime/cef/               Locally installed native CEF distributions
eng/cef/                   Native runtime installation tooling
docs/                      Architecture and implementation notes
```

## Build

```powershell
./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64

dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln --no-restore

dotnet run --project tests/Tarui.Ipc.Tests --no-build
dotnet run --project tests/Tarui.WebView.Tests --no-build
dotnet run --project tests/Tarui.Shell.Tests --no-build
dotnet run --project tests/Tarui.Plugins.Tests --no-build
dotnet run --project tests/Tarui.Hosting.Tests --no-build
dotnet run --project tests/Tarui.Architecture.Tests --no-build

cd web
pnpm install --frozen-lockfile
pnpm lint
pnpm build
```

The Web workspace uses pnpm 11. `web/pnpm-workspace.yaml` defines the `apps/*` and `packages/*` members, and the shared `web/pnpm-lock.yaml` keeps dependency resolution reproducible. The application consumes `@tarui/api` through the `workspace:*` dependency specifier. Run all Web commands from the `web` directory.

CefGlue managed assemblies and native CEF runtime assets are not restored from NuGet. The native runtime installer downloads the official CEF minimal distribution and verifies its published SHA-1. Avalonia itself remains a normal framework package dependency.

For CI and reproducible local setup, use the pinned pnpm 11 toolchain declared by `web/package.json` and install from the lockfile:

```powershell
cd web
pnpm install --frozen-lockfile
```

## Hosting and runtime configuration

`Tarui.App` boots through the Tarui.Hosting builder, which wraps `Microsoft.Extensions.Hosting` and exposes the familiar `Configuration` / `Logging` / `Services` / `Window` members:

```csharp
CefGlueRuntimeBootstrap.RunSubProcess(args); // CEF subprocess dispatch, must run first

var builder = TaruiHost.CreateApplicationBuilder(args);

builder.Services
    .AddTaruiShell()
    .AddCefGlueWebView()
    .AddCorePlugin()
    .AddWindowPlugin()
    .AddEventPlugin()
    .AddDialogPlugin()
    .AddSystemPlugin();

builder.Window.Configure(window =>
{
    window.Title = "tarui.net";
    window.Width = 1280;
    window.Height = 820;
});

builder.Build().Run();
```

Runtime settings load from `appsettings.json` next to the executable, environment variables, and the command line:

- `Tarui:Window:*` — main window title, size, minimum size, centering, and URL. Merge precedence is defaults < configuration < `builder.Window` code.
- `Tarui:Web:*` — WebView resource mode parameters (`Mode`, `Url`, `Root`, `Scheme`, `Host`, `SpaFallback`, `Csp`, `MaxAssetBytes`). The `TARUI_WEB_*` environment variables below remain supported as a fallback.
- `Logging:LogLevel:*` — standard `Microsoft.Extensions.Logging` configuration.

The `capabilities/` directory lives at the repository root; the build copies `capabilities/*.json` into the application output next to `appsettings.json`, and the host resolves both from `AppContext.BaseDirectory`. See `docs/hosting.md` for the full design and the complete configuration key table.

## Web resource modes

HTTP development mode:

```powershell
$env:TARUI_WEB_MODE = "http"
$env:TARUI_WEB_URL = "http://127.0.0.1:5173"
cd web
pnpm dev
```

Local Scheme mode without an HTTP server:

```powershell
cd web
pnpm build
cd ..
$env:TARUI_WEB_MODE = "scheme"
dotnet run --project src/desktop/Tarui.App/Tarui.App.csproj
```

Scheme mode serves `tarui://localhost/index.html`. The build copies Web `dist` files into the application output. Override the defaults with the `Tarui:Web:*` configuration keys or the equivalent environment variables:

- `TARUI_WEB_ROOT`: static asset directory containing `index.html`.
- `TARUI_WEB_SCHEME` / `TARUI_WEB_HOST`: custom origin.
- `TARUI_WEB_SPA_FALLBACK=false`: disable main-frame SPA fallback.
- `TARUI_WEB_CSP`: override the production Content-Security-Policy.
- `TARUI_WEB_MAX_ASSET_BYTES`: maximum asset size, default 64 MiB.

When no mode is specified, a configured `TARUI_WEB_URL` selects HTTP; otherwise a packaged Web directory selects Scheme, falling back to the local development HTTP URL only when no packaged assets exist.

## Tarui CLI

`src/tarui-cli` (`Tarui.Cli`) is a zero-dependency orchestration tool that reads the `tarui.app.json` manifest at the repository root and drives the frontend/backend pipeline. It is published as a `dotnet tool` named `tarui`:

```powershell
dotnet tool install -global Tarui.Cli

tarui init       # scaffold a new app from the tarui-app template (--local <repo> for in-repo dev)
tarui dev        # dev server (build.beforeDevCommand) + dotnet watch, Ctrl+C tears both down
tarui build      # frontend build, self-contained publish, portable zip + latest.json
tarui plugin init <name>   # scaffold a plugin skeleton (permissions/, guest-js/, tests/; --local <repo>)
tarui plugin pack          # plugin pre-flight: layout/permissions/version parity, self-tests, pack both packages
tarui info       # environment / toolchain / manifest diagnostics
tarui --help     # full command surface
```

`tarui dev` starts `build.beforeDevCommand` in `build.frontend`, waits for `build.devUrl` to become reachable, then launches the desktop project with `TARUI_WEB_MODE=http` and `TARUI_WEB_URL=<devUrl>`. `tarui build` runs `build.beforeBuildCommand`, validates `build.frontendDist`, publishes the desktop project self-contained for the current RID, and emits the configured `bundle.targets` (currently a portable zip) plus an updater blueprint `dist/latest.json` with SHA-256. During `build` it also merges every referenced plugin's `permissions/<plugin>/schema.json` into `schemas/permissions.schema.json` (a validation aid only — `capabilities/*.json` remain the sole runtime authorization source). `tarui plugin init` scaffolds a plugin with permission descriptors, a typed guest-js bridge and console self-tests; `tarui plugin pack` validates layout, permission/version consistency, runs self-tests, and packs both the NuGet backend (with `permissions/`) and the npm frontend. Run from the repository root; see `docs/dev-workflow-design.md` for the manifest schema and the phased rollout (W3 application templates / `tarui init` complete, W4 plugin workflow complete, W5 installers).

## Native capability surface

`AddTaruiShell()` composes the shell from explicitly registered plugins. Every command is permission-checked against the capability file of the calling window (`capabilities/main.json`):

| Plugin | Commands | Surface |
| --- | --- | --- |
| Core | `core:app|get-info` | Shell handshake: product, version, capabilities. |
| Window | `core:window|*` (24) | Create/close/minimize/maximize/hide/show/focus/center, title, size, position, min/max size, always-on-top, resizable, decorations, fullscreen, state, monitors, list. |
| Event | `core:event|emit` | Emit routed or broadcast events from the Web side. |
| Dialog | `plugin:dialog|open`, `plugin:dialog|save` | Native file/directory pickers attached to the requesting window. |
| System | `core:path|resolve`, `core:os|info`, `core:process|exit`, `core:process|relaunch`, `core:shell|open`, `core:clipboard|read-text`, `core:clipboard|write-text` | Path resolution with escape protection, OS info, process lifecycle, OS default handler, clipboard text. |

The shell routes window lifecycle events to the owning Webview (`window://moved`, `window://resized`, `window://focus-changed`, `window://close-requested`) and broadcasts `window://destroyed` and `shell://theme-changed` to every window. Closing is cooperative: the title-bar close request is delivered as an event and the Web side confirms by invoking `core:window|close`.

## Frontend bridge

`web/packages/api` (`@tarui/api`) ships one typed module per plugin contract: `ipc`, `app`, `window`, `event`, `dialog`, `os`, `path`, `process`, `shell`, and `clipboard`. `Window.getCurrent()` returns a handle whose label-less calls target the window hosting the calling Webview; `Window.getByLabel`/`Window.create` address other windows. The barrel export renames the two `open` helpers to `openDialog` and `openExternal`; subpath exports such as `@tarui/api/window` keep the Tauri-style short names.

## CefGlue port

The source port is based on upstream commit `e3389315dad795374be1a1e52c42d4e49cb6fe7b`, CEF `150.0.11`, and targets Avalonia `12.1.1`. Reflection-based ObjectBinding, generic JavaScript evaluation, ReactiveUI, and System.Reactive were removed. Tarui IPC enters through the fixed `window.invokeCSharpAction` CEF process-message bridge.

The current port supports native windowed rendering. OSR and its Avalonia 11 drag-and-drop layer are intentionally excluded.
