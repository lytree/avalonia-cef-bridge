# Tarui Application

This is a [Tarui](https://tarui.dev) desktop application scaffolded from the
`tarui-app` template. It pairs a .NET desktop host (`*.Desktop`) with a
React + TypeScript frontend (`web/`) rendered inside the Tarui WebView.

## Prerequisites

- .NET 10 SDK
- Node.js (20+) and pnpm
- CEF runtime (`runtime/cef/<rid>`) or a path via the `TARUI_CEF_ROOT`
  environment variable

## Scaffolding

```powershell
# New app (preferred; also runs name normalization and pnpm install)
tarui init my-app

# In-repo development without published packages
tarui init my-app --local <path-to-tarui-source>

# Or use the underlying template directly
dotnet new tarui-app -n Demo -o Demo
```

## Develop

```powershell
tarui dev    # frontend dev server + desktop host with hot reload
```

## Build

```powershell
tarui build  # frontend build + dotnet publish + zip bundle (dist/)
```

## Layout

```text
<app>/
  <app>.Desktop/          # .NET host: Program.cs + appsettings.json + manifest
  web/                    # React + TypeScript frontend (Vite)
  capabilities/main.json  # minimal, default-deny capability set
  tarui.app.json          # build-time manifest consumed by the CLI
```

The scaffold ships **zero plugins** and a minimal capability set: the desktop
matches the core shell APIs only. Grant additional plugin permissions
explicitly in `capabilities/*.json`.