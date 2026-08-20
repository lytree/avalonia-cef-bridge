# AGENTS.md

Conventions for AI-assisted work in this repository.

## Commands

Run from the repository root unless noted:

```powershell
# .NET (requires CEF native runtime installed once: ./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64)
dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln --no-restore

dotnet run --project tests/Tarui.Ipc.Tests --no-build
dotnet run --project tests/Tarui.WebView.Tests --no-build
dotnet run --project tests/Tarui.Shell.Tests --no-build
dotnet run --project tests/Tarui.Plugins.Tests --no-build
dotnet run --project tests/Tarui.Hosting.Tests --no-build
dotnet run --project tests/Tarui.Architecture.Tests --no-build

# Web (run from web/)
pnpm install --frozen-lockfile
pnpm lint
pnpm build
```

`TreatWarningsAsErrors` is on for the whole solution; the architecture gate (`Tarui.Architecture.Tests`) enforces layering rules over all active files. Both must stay green before a task is considered done.

## Workflow rules

- Every phase requires complete unit tests before moving to the next implementation step.
- Update README.md, docs/architecture.md, and this file whenever behavior, contracts, or commands change.
- Update the TODO list after each implementation step.
- Code tasks first; documentation phases follow without intermediate confirmation.

## Architecture invariants

- No runtime reflection, assembly scanning, dynamic plugin loading, or JSON reflection fallback. Wire DTOs use `JsonSerializerContext` source generation; register new DTOs in `TaruiJsonContext`.
- Plugins are project references registered explicitly at the composition root via `AddPlugin<T>()` / `Add*Plugin()` — compile-time registration, no assembly scanning. Adding a command means: DTO record in `Tarui.Contracts`, `TaruiJsonContext` registration, handler wired in the plugin class's `ConfigureCommands(CommandRouterBuilder)`, `commands.Add` with its permission, and the permission in the target capability file.
- The shell-side `CommandContext` label is authoritative; never trust the Web envelope's window label for routing decisions.
- Every command is permission-checked against the calling window's capability set (`capabilities/*.json`). Unknown permissions in capability files fail startup.
- Closing is cooperative: OS close requests are cancelled and delivered as `window://close-requested`; only `core:window|close` destroys the window.
- The frontend consumes native capabilities only through `@tarui/api` typed modules; keep module files aligned one-to-one with plugin contracts.
- The Web workspace is pnpm 11 with `workspace:*` linking; run `pnpm lint` and `pnpm build` in `web/` after touching TypeScript.
