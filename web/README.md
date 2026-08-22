# tarui.net Web workspace

- `apps/Tarui.Web`: deployable React business application; demonstrates every typed API module (window controls, dialogs, clipboard, shell, paths, events).
- `packages/api`: `@lytree/api` typed bridge. One module per native plugin contract: `ipc` (raw `invoke`/`listen`/`Channel`), `app`, `window`, `event`, `dialog`, `os`, `path`, `process`, `shell`, `clipboard`.
Import either from the barrel (`@lytree/api`, where `dialog.open`/`shell.open` are renamed `openDialog`/`openExternal`) or via subpaths with Tauri-style short names (`@lytree/api/window`). `Window.getCurrent()` targets the window hosting the calling Webview; `Window.getByLabel`/`Window.create` address other windows.
This is a pnpm 11 workspace. `pnpm-workspace.yaml` includes `apps/*` and `packages/*`, while the shared `pnpm-lock.yaml` keeps installs reproducible. `apps/Tarui.Web` consumes `@lytree/api` from `packages/api` with the `workspace:*` dependency specifier.

Run all commands from this `web` directory:

```powershell
pnpm install --frozen-lockfile
pnpm dev
pnpm lint
pnpm build
```

`pnpm dev` starts the HTTP development server. `pnpm lint` checks the Web workspace, and `pnpm build` produces the deployable `dist` output used by Scheme mode. CI should use `pnpm install --frozen-lockfile` before running the checks or build.
