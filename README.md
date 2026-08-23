# tarui.net

`tarui.net` combines an Avalonia native shell with a React/TypeScript business UI and the standalone `CefGlue.Next.Avalonia` browser component.

## Architecture

- Avalonia owns the native window shell and platform components.
- `CefGlue.Next.Avalonia` is the only Tarui-side entry point for the vendored CefGlue implementation and Avalonia browser control.
- `Tarui.WebView.CefGlueNext` adapts that component to Tarui policies, IPC and events; Shell and Hosting do not reference CefGlue types.
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
    CefGlue.Next.Avalonia/  Standalone Avalonia browser component and runtime lifecycle
    Tarui.WebView.*         Tarui browser contracts and component adapter
web/
  apps/Tarui.Web/          React business application
  packages/api/            @lytree/api bridge package
tests/                     Executable and integration tests
capabilities/              Window/WebView permission manifests
runtime/cef/               Locally installed native CEF distributions
examples/demo/             In-repo demo app (local ProjectReference to src/)
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

dotnet pack tarui.net.sln -c Release -o artifacts/nuget
dotnet run --project tests/Tarui.Architecture.Tests --no-build -- --require-package --package artifacts/nuget/CefGlue.Next.Avalonia.0.1.0.nupkg

cd web
pnpm install --frozen-lockfile
pnpm lint
pnpm build
```

The Web workspace uses pnpm 11. `web/pnpm-workspace.yaml` defines the `apps/*` and `packages/*` members, and the shared `web/pnpm-lock.yaml` keeps dependency resolution reproducible. The application consumes `@lytree/api` through the `workspace:*` dependency specifier. Run all Web commands from the `web` directory.

CefGlue managed assemblies and native CEF runtime assets are not restored from NuGet. The native runtime installer downloads the official CEF minimal distribution and verifies its published SHA-1. Avalonia itself remains a normal framework package dependency.

For CI and reproducible local setup, use the pinned pnpm 11 toolchain declared by `web/package.json` and install from the lockfile:

```powershell
cd web
pnpm install --frozen-lockfile
```

## Hosting and runtime configuration

`Tarui.App` boots through the Tarui.Hosting builder, which wraps `Microsoft.Extensions.Hosting` and exposes the familiar `Configuration` / `Logging` / `Services` / `Window` members. The CEF subprocess dispatch belongs to `CefGlue.Next.Avalonia`:

```csharp
using CefGlue.Next.Avalonia;
using Tarui.Hosting;
using Tarui.Shell;

if (CefGlueNextAvaloniaRuntime.RunSubProcess(args))
{
    return;
}

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

try
{
    builder.Build().Run();
}
finally
{
    CefGlueNextAvaloniaRuntime.Shutdown();
}
```

## WebView component boundaries

The browser stack is intentionally split into four layers:

| Layer | Responsibility | May reference |
| --- | --- | --- |
| `Tarui.WebView.Abstractions` | UI-neutral navigation, script, download, file-drop and drag-region contracts | no Avalonia, no CefGlue |
| `Tarui.WebView.Avalonia` | `Control`-bearing Avalonia contract | Avalonia + Tarui WebView contracts |
| `CefGlue.Next.Avalonia` | Direct Avalonia browser control, CefGlue handlers, runtime and native browser lifecycle | Avalonia + vendored CefGlue |
| `Tarui.WebView.CefGlueNext` | Tarui configuration, IPC, capability policy and event translation | Tarui contracts + `CefGlue.Next.Avalonia` |

For a direct Avalonia application, install `CefGlue.Next.Avalonia`, call `CefGlueNextAvaloniaRuntime.RunSubProcess(args)` before host startup, initialize one runtime configuration, embed `CefGlueNextAvaloniaWebView`, and await every WebView close before leaving the Avalonia loop. The application then stops and disposes its Host and calls `CefGlueNextAvaloniaRuntime.Shutdown()` from `Program`'s `finally` block. Tarui applications normally use `Tarui.WebView.CefGlueNext` so the Shell can apply window capabilities and IPC policy.

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
tarui build      # frontend build, self-contained publish, zip/msix bundles + latest.json
tarui plugin init <name>   # scaffold a plugin skeleton (permissions/, guest-js/, tests/; --local <repo>)
tarui plugin pack          # plugin pre-flight: layout/permissions/version parity, self-tests, pack both packages
tarui info       # environment / toolchain / manifest diagnostics
tarui --help     # full command surface
```

`tarui dev` starts `build.beforeDevCommand` in `build.frontend`, waits for `build.devUrl` to become reachable, then launches the desktop project with `TARUI_WEB_MODE=http` and `TARUI_WEB_URL=<devUrl>`. `tarui build` runs `build.beforeBuildCommand`, validates `build.frontendDist`, publishes the desktop project self-contained for the current RID, then produces the configured `bundle.targets` — a portable `zip` plus an MSIX (`--bundle msix`, or `bundle.targets: ["zip","msix"]`) — and an updater blueprint `dist/latest.json` with SHA-256. The MSIX is built by the managed `MsixPacker` (OPC ZIP + `AppxManifest.xml` + SHA-256 `AppxBlockMap.xml`, no `makeappx` dependency); if `bundle.msix.certificate.{path,password,timeStamperUrl}` is configured it is Authenticode-signed via `signtool.exe`, otherwise it is emitted unsigned. During `build` it also merges every referenced plugin's `permissions/<plugin>/schema.json` into `schemas/permissions.schema.json` (a validation aid only — `capabilities/*.json` remain the sole runtime authorization source). `tarui plugin init` scaffolds a plugin with permission descriptors, a typed guest-js bridge and console self-tests; `tarui plugin pack` validates layout, permission/version consistency, runs self-tests, and packs both the NuGet backend (with `permissions/`) and the npm frontend. Run from the repository root; see `docs/dev-workflow-design.md` for the manifest schema and the phased rollout (W3 application templates / `tarui init` complete, W4 plugin workflow complete, W5 installers complete).

## Demo app

[`examples/demo`](examples/demo) is an in-repo sample application that wires the desktop host, the `core:window|*`, `core:event|emit`, `plugin:fs|*` and `plugin:store|*` plugins, and the `@lytree/api` frontend bridge end to end. It builds against the local `src/` tree via `ProjectReference` (no published packages), so it always tracks the current source.

```powershell
cd examples/demo/Demo.Desktop
dotnet run --project Demo.Desktop.csproj
```

The React UI (`examples/demo/web`) demonstrates window+IPC state control, routed events, and isolated `appData` store/fs access. Its `capabilities/main.json` grants only the permissions the demo exercises.

## CI and releases

GitHub Actions automates the integration and release gates (design `§10`):

- `.github/workflows/ci.yml` — PR / branch gate: `dotnet build` 0 warnings, package/nuspec validation for `CefGlue.Next.Avalonia`, an external NuGet consumer restore/build smoke test, all self-tests, `Tarui.Architecture.Tests`, version consistency (`Directory.Build.props` == `@lytree/api`), and `pnpm lint` + `pnpm build`.
- `.github/workflows/release.yml` — tag `tarui-v<version>` (or manual dispatch): performs the same component package and external-consumer gates before pushing NuGet packages, publishes `@lytree/api`, builds the `zip;msix` installers on Windows (optional Authenticode), and creates a GitHub Release with the artifacts attached.

Release secrets live in the GitHub `release` environment. NuGet publishing uses [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) (OIDC, no long-lived API key): allowlist the `release` environment and the `release.yml` workflow filename on nuget.org, then add the `NUGET_USER` env secret (nuget.org profile name, not email). `@lytree/api` is published to npm with [provenance](https://docs.npmjs.com/generating-provenance-statements) (OIDC — add a `NPM_USER`-associated trusted-publisher entry for this repo on npmjs.com; no `NPM_TOKEN` needed). Optional: `NUGET_SOURCE`. Optional for signed MSIX: `WINDOWS_CERT_BASE64`, `WINDOWS_CERT_PUBLISHER`, `WINDOWS_CERT_PASSWORD`, `WINDOWS_CERT_TIMESTAMP`; without a certificate the MSIX is emitted unsigned.

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

`web/packages/api` (`@lytree/api`) ships one typed module per plugin contract: `ipc`, `app`, `window`, `event`, `dialog`, `os`, `path`, `process`, `shell`, and `clipboard`. `Window.getCurrent()` returns a handle whose label-less calls target the window hosting the calling Webview; `Window.getByLabel`/`Window.create` address other windows. The barrel export renames the two `open` helpers to `openDialog` and `openExternal`; subpath exports such as `@lytree/api/window` keep the Tauri-style short names.

## CefGlue port

The source port is based on upstream commit `e3389315dad795374be1a1e52c42d4e49cb6fe7b`, CEF `150.0.11`, and targets Avalonia `12.1.1`. Reflection-based ObjectBinding, generic JavaScript evaluation, ReactiveUI, and System.Reactive were removed. Tarui IPC enters through the fixed `window.invokeCSharpAction` CEF process-message bridge.

The current port supports native windowed rendering through `CefGlue.Next.Avalonia`. OSR and its Avalonia 11 drag-and-drop layer are intentionally excluded. The managed component package embeds all required Xilium CefGlue assemblies and intentionally has no Xilium package dependency; native CEF files remain application/runtime assets installed by `eng/cef/install-runtime.ps1` or a future RID runtime package.
