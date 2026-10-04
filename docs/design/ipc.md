# IPC 模型

> Command / Event / Channel 三类 IPC、能力闸门、错误码、DTO 元数据。

## 1. 模型

Tarui IPC 由四类原语构成:

| 原语 | 方向 | 形态 | 用途 |
| --- | --- | --- | --- |
| **Command** | Web → Host → Web | 请求/响应,JSON over CEF process message | 业务动作(窗口控制、文件读写、HTTP 调用、…) |
| **Event** | Web ↔ Host | 低频通知 | 状态广播、生命周期事件、用户自定义 |
| **Channel** | Host ↔ Web | 命令关联的有序进度流 | 大文件流、HTTP 流、Shell stdio |
| **Capability** | 静态授权层 | 每个窗口一份 capability 清单 | 命令/事件白名单 |

`@lytree/api` 为每条命令提供强类型模块;前端禁止裸 `invoke(...)`,所有调用必须走 `ipc` / `window` / `fs` 等模块以保留类型。

## 2. 线协议

### 2.1 DTO 元数据

跨进程序列化的 DTO 全部使用 `System.Text.Json` 源生成元数据(`JsonSerializerContext`),禁止 `JsonSerializer.Serialize(obj)` 的反射回退路径。`src/core/Tarui.Contracts/Tarui.Contracts.csproj` 集中暴露所有命令的入参 / 出参 record + `[JsonSerializable(typeof(...))]` 标注。

### 2.2 CEF 进程消息桥

Web 侧通过固定的 `window.invokeCSharpAction(json)` 入口发起调用,经 `__taruiIpc` CEF process message 上行;`Tarui.WebView.CefGlueNext` 把消息提升为 `TaruiWebMessage`,送入 `IpcDispatcher` → `CommandRouter`。响应通过已编码的 JavaScript 在 WebView 内派发。

```text
Web: window.invokeCSharpAction(jsonPayload)
  ↓
CEF process message "__taruiIpc"
  ↓
Tarui: TaruiWebMessage → IpcDispatcher.DispatchAsync(CommandContext)
  ↓
CommandRouter.TryGetCommand(id) → registered handler
  ↓
Cap-check → Result / Error → JS dispatch callback
```

### 2.3 Channel

Channel 是 Tarui 端到端流式 IPC 的核心:

```text
Web: invoke("plugin:fs|read-file-stream", { path }, { onMessage, onClose })
  ↓
Host: 解析 onMessage/onClose → TaruiChannel<JsonElement> token
  ↓
Handler: foreach (chunk in stream) await channel.SendAsync(chunk)
  ↓
WebviewSession: ExecuteScriptAsync(`window.__taruiChannelInvoke("token", chunk)`)
  ↓
Web: Channel.onmessage(chunk) → … → Channel.onclose()
```

背压由 `WebviewSession.ExecuteScriptAsync` await 天然成立:JS 调度满时,`SendAsync` 自动 await,生产者自然减速。`WebviewAttacher` 还维护 `ChannelTokenRegistry`,确保 `SendAsync` 在 WebView 未就绪时延迟绑定。

## 3. 能力闸门

### 3.1 命令权限

每条命令通过 `commands.Add(name, argsTypeInfo, resultTypeInfo, handler, permission, scopeAuthorizer)` 注册,permission ID 形如 `core:app|get-info`、`plugin:fs|read-text-file`。`CommandRouterBuilder.RegisteredPermissions` 是去重集合,在 `CommandRouterComposer.Compose(...)` 阶段比对每个 capability 文件中显式列出的 permission;**未注册权限会被架构门禁抛 `InvalidOperationException`,跳过 `"*"` 通配语义**。

### 3.2 Capability 文件

每个窗口/WebView 持有标识符(如 `main`),仅允许执行 capability 清单中显式列入的命令与事件。**缺省即拒绝**(无显式 capability = 拒绝一切)。

```jsonc
// capabilities/main.json
{
  "identifier": "main",
  "windows": ["main"],
  "platforms": ["windows", "macos", "linux"],
  "events": [
    "window://moved", "window://resized", "window://focus-changed",
    "window://close-requested", "shell://theme-changed",
    "tray://clicked", "notification://activated",
    "deeplink://tarui", "updater://status", "demo://echo"
  ],
  "permissions": [
    "core:app|get-info",
    "core:window|set-title",
    "core:window|set-size",
    {
      "identifier": "plugin:store|set",
      "allow": [
        { "base": "appData", "path": "settings.json" },
        { "base": "appConfig", "path": "**/*.json" }
      ]
    }
  ]
}
```

字段语义:

| 字段 | 作用 |
| --- | --- |
| `identifier` | capability 唯一标识 |
| `windows` | 适用的窗口 label 列表 |
| `platforms` | 平台白名单(`windows`/`macos`/`linux`) |
| `events` | 允许 **接收** 的事件(原生 + 自定义) |
| `permissions` | 允许 **调用** 的命令,字符串=全权;对象=细化 scope |

### 3.3 Scope allow / deny

- FileSystem / GlobalShortcut / Store 等插件支持 `allow` + `deny` 同存,deny 命中即拒绝。
- `base` 字段枚举:`appData` / `appConfig` / `appCache` / `appLog` / `temp` / `resources`;`path` 走 glob。
- `Persisted Scope`(`IRuntimeScopeOverlay`)在 IPC 层把运行时持久化的 scope 与静态 capability scope 合并,deny 优先。

### 3.4 多窗口隔离

`create-window` 时只有两个窗口的 ID / events / permissions 完全匹配时才允许;创建者上下文通过 `CommandContext.Capabilities` 做提权防护。

### 3.5 协作式关闭

title-bar 关闭请求先发出 `window://close-requested` 事件;前端必须显式调用 `core:window|close`(或 `core:window|deny-close` 取消强制回退)才真正退出。`Tarui:Window:CloseRequestTimeout` 控制超时;默认 3 秒后强制关闭。

## 4. 错误码语义

`IpcCommandError` 携带稳定的错误码字符串,前端用 `code` 判断分支:

| 错误码 | 含义 |
| --- | --- |
| `not_authorized` | 窗口 capability 未授予该命令 |
| `not_found` | 命令未注册或窗口不存在 |
| `invalid_payload` | DTO 校验失败 |
| `scope_denied` | scope allow/deny 命中拒绝 |
| `not_supported` | 当前平台不支持该能力 |
| `resource_conflict` | 系统占用或冲突 |
| `user_denied` | 用户拒绝系统授权 |
| `io_error` | 文件 / 网络 / 进程 I/O 错误 |

前端不应依赖错误文本(本地化、漂移);错误码是契约,跨版本稳定。

## 5. 前端桥接

`web/packages/api`(`@lytree/api`)为每个插件契约提供强类型模块;主入口和子路径导出均支持:

```ts
import { invoke } from "@lytree/api/ipc"
import { getAppInfo } from "@lytree/api/app"
import { getCurrentWindow } from "@lytree/api/window"
import { emit, listen } from "@lytree/api/event"
import { openDialog } from "@lytree/api/dialog"        // openExternal 也在 dialog barrel
import { openExternal } from "@lytree/api/shell"      // OS 默认处理器
import { fs, store, log } from "@lytree/api/fs"       // 命名空间风格
```

`Window.getCurrent()` 无 label 时指向当前 Webview 所在窗口,`getByLabel('editor')` 寻址其它窗口。生命周期订阅(`onMoved` / `onResized` / `onFocusChanged` / `onCloseRequested` / `onDestroyed`)统一包装 `listen`;`Channel` 通过 `stream` 工具与原生 `plugin:fs|read-file-stream` 等命令对接。

## 6. 事件命名空间

- **原生前缀保留**:`app://`、`window://`、`shell://`、`webview://`、`tray://`、`notification://`、`menu://`、`global-shortcut://`、`updater://`、`fs://`、`log://`、`scope://`、`deeplink://`、`core://` —— Web 不可发起。
- **Web 限定 `user://`** —— Web 侧 `core:event|emit` 仅允许 `user://*` 前缀(架构门禁)。
- **接收授权**:每个 capability 的 `events` 列表显式声明可接收事件;`user://*` 默认接收,保留原生前缀必须显式列入。

## 7. 修改 IPC 协议

IPC 是契约,改动需三处同步且保持向后兼容:

| 位置 | 文件 | 改动 |
| --- | --- | --- |
| 后端 DTO | `src/core/Tarui.Contracts/**` | 新增 record,实现 `ITaruiCommand<TArg,TRes>` 或事件 record |
| JSON 元数据 | 同上,加 `[JsonSerializable(typeof(...))]` 进 `TaruiJsonContext` | 必须 |
| 后端处理器 | `src/plugins/Tarui.Plugins.Foo/FooPlugin.cs` | `ConfigureCommands` 中 `commands.Add("plugin:foo|do", DoHandler, "plugin:foo|do")` |
| 前端桥接 | `web/packages/api/src/foo.ts` | 类型 + 调用包装 |
| 包导出 | `web/packages/api/package.json` | 新增 `"./foo"` 子路径 |
| 能力清单 | `examples/demo/capabilities/*.json` 或用户 app 的 `capabilities/*.json` | 加入 `plugin:foo|do` |

禁止:

- 在 `IpcDispatcher` 之外添加全局 IPC 入口(避免绕开 capability 校验)。
- 用反射序列化 DTO(无 `JsonSerializerContext` 标注)。
- 在前端用裸 `invoke('plugin:foo|do', ...)` 取代 `@lytree/api/foo`(失去类型)。
