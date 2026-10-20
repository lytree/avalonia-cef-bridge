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

### 2.1 生成的命令契约 `generated/commands.ts`

`Tarui.Ipc.Generators` 从 `commands.Add("id", …, "permission")` 的调用点生成每程序集 `{Assembly}CommandCatalog`,同时产出两份元数据:

- `Contracts`(`TaruiCommandContract`):command id、入参 / 出参 DTO 名、门控 permission;
- `Types`(`TaruiTypeContract`):入参 / 出参的 **JSON 形状**。

`Types` 由 `DtoShapeReader` 在编译期解析 `TaruiJsonContext.Default.{Name}` 背后的 `JsonTypeInfo<T>` 得到,映射规则与序列化器设置一致:

| C# | TypeScript | 说明 |
| --- | --- | --- |
| `string` / `bool` / 数值 / `char` | `string` / `boolean` / `number` / `string` | |
| `string?`、`long?` 等**可空成员** | `readonly path?: string` | `DefaultIgnoreCondition = WhenWritingNull` ⇒ 写成可选属性,而不是 `string \| null` |
| `byte[]` | `string` | STJ 序列化为 base64 |
| `T[]`、`IReadOnlyList<T>`、`ImmutableArray<T>` | `T[]` | |
| `IReadOnlyDictionary<string, T>` | `Record<string, T>` | |
| `JsonElement`、未建模泛型 | `unknown` | 不猜形状 |
| 枚举 | `number` | `TaruiJsonContext` 未注册 `JsonStringEnumConverter` |
| `DateTime` / `Guid` / `Uri` 等 | `string` | |
| 嵌套 DTO | 同名 interface | 递归展开;自引用直接引用自身(`MenuItemDefinition.items?: MenuItemDefinition[]`) |

成员名按 `PropertyNamingPolicy = CamelCase` 生成(含首字母缩写:`IOStream` → `ioStream`),`[JsonPropertyName]` 显式名优先;空 DTO(`EmptyArgs`、`Unit`)生成 `export type X = Record<string, never>`。

[`tests/Tarui.Bindings.Tests`](../../tests/Tarui.Bindings.Tests) 汇总全部 catalog,产出并校验 `web/packages/api/generated/commands.ts`:

```ts
export interface FsPathOptions {
  readonly base: string
  readonly path?: string
}

export interface TaruiCommandContracts { /* id → permission + DTO 名 */ }
export interface TaruiCommandTypes {
  readonly "plugin:fs|read-text-file": {
    readonly input: FsPathOptions
    readonly output: FsReadTextResult
  }
  // …
}

export type TaruiCommandId = keyof TaruiCommandContracts
export type TaruiCommandInput<T extends TaruiCommandId> = TaruiCommandTypes[T]['input']
export type TaruiCommandResult<T extends TaruiCommandId> = TaruiCommandTypes[T]['output']
```

调用侧用类型化入口即可,与 `invoke` 共用同一条桥,只是命令 id、入参与返回值都受契约约束:

```ts
import { invokeTyped, invokeTypedEmpty } from '@lytree/api'

const result = await invokeTyped('plugin:fs|read-text-file', { base: 'appData', path: 'a.txt' })
result.contents // string

const state = await invokeTypedEmpty('plugin:autostart|is-enabled')
state.enabled // boolean
```

契约漂移门禁:

```powershell
dotnet run --project tests/Tarui.Bindings.Tests            # 校验(CI)
dotnet run --project tests/Tarui.Bindings.Tests -- --update # 原生命令或 DTO 变更后重生成
```

同一个门禁还会校验:前端 `invoke('…')` 字面量必须都在生成契约内;每个生成命令都被前端调用或列在 `InternalOnlyCommands` 白名单里;每个命令的入参 / 出参都必须解析到类型形状;同一个 catalog 名不得描述两种不同形状;形状里的成员行必须以 `;` 结尾。

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
