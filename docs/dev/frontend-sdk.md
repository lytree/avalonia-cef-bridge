# 前端 SDK `@lytree/api`

> `@lytree/api` 桥接包开发、模块命名约定、版本对齐。

## 1. 工作区结构

```text
web/
  apps/Tarui.Web/         # React 业务应用(@lytree/web),用 workspace:* 引用 @lytree/api
  packages/api/           # @lytree/api,纯 ESM,提供 ipc/app/window/... 子路径
```

- TypeScript 6.0.x,严格 ESM,React 19,Vitest 3.2.4。
- `pnpm dev` / `pnpm build` / `pnpm lint` / `pnpm preview` 由 `web/package.json` 提供。
- 包名锁定 `@lytree/api`,版本必须与 `Directory.Build.props` 的 `TaruiVersion` 一致(CI `Version consistency` 步骤守护)。
- 命名约定:`openDialog`/`openExternal` 在 barrel 中区分(都叫 `open` 时);`fs`/`store`/`log` 走 namespace 风格;`window.getCurrent()` 无 label 时指向当前 webview。

## 2. 包结构

`web/packages/api/package.json`:

```jsonc
{
  "name": "@lytree/api",
  "version": "<TaruiVersion>",
  "type": "module",
  "main": "./dist/index.js",
  "types": "./dist/index.d.ts",
  "exports": {
    ".": { "types": "./dist/index.d.ts", "default": "./dist/index.js" },
    "./ipc":  { "types": "./dist/ipc.d.ts",  "default": "./dist/ipc.js" },
    "./app":  { "types": "./dist/app.d.ts",  "default": "./dist/app.js" },
    "./window": { "types": "./dist/window.d.ts", "default": "./dist/window.js" },
    // ... 30+ 子路径
  },
  "files": ["dist"],
  "publishConfig": { "access": "public" },
  "scripts": {
    "build": "tsc -b",
    "prepack": "tsc -b"
  }
}
```

构建:`tsc -b`(`composite` + `declaration` + `sourcemap`,ESM-only),产物 `dist/` 保留子路径结构。零新增构建依赖。

## 3. 新增模块步骤

1. 在 `web/packages/api/src/<name>.ts` 创建模块并定义类型 + `invoke` 包装。
2. 在 `web/packages/api/src/index.ts` barrel 中导出(必要时重命名)。
3. 在 `web/packages/api/package.json` 的 `exports` 添加 `"./<name>"`。
4. 在 `web/packages/api/__tests__` 加 Vitest 单元测试(契约 stub 即可)。
5. `pnpm build` + `pnpm lint` 通过。

## 4. 命名约定

| 模块 | 命令前缀 | TS 模块命名 | 命名风格 |
| --- | --- | --- | --- |
| `core:app` | `core:app|get-info` | `@lytree/api/app` | 函数式 |
| `core:window` | `core:window|*` | `@lytree/api/window` | 类(Window) |
| `core:event` | `core:event|emit` | `@lytree/api/event` | 函数式 |
| `core:os` / `path` / `process` / `shell` / `clipboard` | 各自前缀 | `@lytree/api/{os,path,process,shell,clipboard}` | 函数式 |
| `core:cli` / `core:channel` | 各自前缀 | `@lytree/api/{cli,channel}` | 函数式 |
| `core:platform` | `core:platform|capabilities` | `@lytree/api/platform` | 函数式 |
| `plugin:fs` | `plugin:fs|*` | `@lytree/api/fs` | namespace |
| `plugin:store` | `plugin:store|*` | `@lytree/api/store` | namespace |
| `plugin:log` | `plugin:log|record` | `@lytree/api/log` | namespace |
| `plugin:menu` / `tray` | 各自前缀 | `@lytree/api/{menu,tray}` | 类(Menu / TrayIcon) |
| `plugin:dialog` | `plugin:dialog|*` | `@lytree/api/dialog` | 函数式 |
| `plugin:window-state` / `single-instance` | 各自前缀 | `@lytree/api/{window-state,single-instance}` | 函数式 |
| `plugin:notification` | `plugin:notification|*` | `@lytree/api/notification` | 类(Notification) |
| `plugin:autostart` / `global-shortcut` | 各自前缀 | `@lytree/api/{autostart,global-shortcut}` | 函数式 |
| `plugin:deep-link` | `plugin:deep-link|*` | `@lytree/api/deep-link` | 函数式 |
| `plugin:updater` | `plugin:updater|*` | `@lytree/api/updater` | 类(Updater) |
| `plugin:http` / `websocket` | 各自前缀 | `@lytree/api/{http,websocket}` | 类(HTTP / TaruiWebSocket) |
| `plugin:cookie` / `positioner` / `persisted-scope` | 各自前缀 | `@lytree/api/{cookie,positioner,persisted-scope}` | 函数式 |
| `plugin:webview` | `plugin:webview|*` | `@lytree/api/webview` | 类(Webview) |

`Window.getCurrent()` 无 label 时指向当前 webview 所在窗口,`getByLabel('editor')` 寻址其它窗口。Barrel 导出把两个 `open` helper 重命名为 `openDialog` 与 `openExternal`;子路径导出保留 Tauri 风格的简短名。

## 5. 版本对齐与发布

- `web/packages/api/package.json#version` 必须等于 `Directory.Build.props#TaruiVersion`。CI `Version consistency` 步骤失败即拒。
- 发布:tag `tarui-v<version>` 触发 `release.yml` 的 npm 发布 job,使用 OIDC provenance(无需 `NPM_TOKEN`)。
- `@lytree/api` 与 `Tarui.*` NuGet 包族 lockstep 推进。
