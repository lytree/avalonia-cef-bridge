# @tarui/web

React/TypeScript business UI hosted by the tarui.net desktop shell.

The application is part of the pnpm 11 workspace declared by `web/pnpm-workspace.yaml`. Its dependencies are resolved from the shared `web/pnpm-lock.yaml`, and `@tarui/api` is linked from `web/packages/api` through the `workspace:*` dependency specifier.

Run commands from the `web` workspace root:

```powershell
pnpm install --frozen-lockfile
pnpm dev
pnpm lint
pnpm build
```

`pnpm dev` serves the HTTP development entry point. `pnpm lint` runs the application lint check, and `pnpm build` creates the `dist` directory consumed by the desktop shell in Scheme mode. Use `pnpm install --frozen-lockfile` in CI for a reproducible install.
