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

背压的真实边界:`WebviewSession.SendAsync` 只 await 到 `ExecuteScriptAsync` 把脚本提交给 UI 线程/浏览器队列。CEF 适配层对该调用是 fire-and-forget,它不等待渲染进程执行 `Channel.onmessage`,响应路径也没有消费回执;`TaruiChannel<T>` 的绑定路径与无 sink 的内存路径都是无界缓冲。因此生产者只受「脚本提交节奏」约束,渲染侧消费不受限——慢消费者会让在途帧持续累积。可协商的信用额度/ACK 背压(`channel-credit-v1`)在 `tauri-v2-refactor-design.md` R3-A 中设计;落地前不要把当前行为当作有界流控。`WebviewAttacher` 仍维护 `ChannelTokenRegistry`,确保 `SendAsync` 在 WebView 未就绪时延迟绑定。

## 3. 能力闸门

### 3.1 命令权限

每条命令通过 `commands.Add(name, argsTypeInfo, resultTypeInfo, handler, permission, scopeAuthorizer)` 注册,permission ID 形如 `core:app|get-info`、`plugin:fs|read-text-file`。`CommandRouterBuilder.RegisteredPermissions` 是去重集合,在 `CommandRouterComposer.Compose(...)` 阶段比对每个 capability 文件中显式列出的 permission;**未注册权限会被架构门禁抛 `InvalidOperationException`,跳过 `"*"` 通配语义**。

### 3.1.1 权限集合(ACL set)

capability 不必逐条罗列命令:manifest 可以引用**权限集合**,在加载期展开为具体 permission ID(对应 Tauri v3 的 ACL 集合与「按需物化 manifest commands」)。

- **命名规约**:集合标识 = `{命名空间}|{后缀}`,命名空间是 permission ID 最后一个 `|` 之前的部分(`plugin:fs|read-text-file` → `plugin:fs`);没有 `core:` / `plugin:` 前缀的 permission 属于 `app` 命名空间。
- **自动物化**:每个命名空间生成一个 `{命名空间}|default`(该命名空间除跨目标守卫外的全部 permission),跨目标守卫单独分组为 `{命名空间}|other-window` / `{命名空间}|other-webview`——`|default` 永不包含跨越其他窗口 / WebView 的守卫权限。应用自有命令因此天然获得隐式 `app|default`。
- **定制与豁免**:插件可 `DefinePermissionSet(...)` 覆盖同名自动集合,或 `ExcludeFromDefaultSet(permission)` 把必须显式授予的权限(如 `plugin:webview|send-devtools-message` 的 CDP 通道)排除在 `|default` 之外。
- **禁止携带 scope**:集合条目只表达「授予哪些 permission」,`allow` / `deny` 必须落到单条 permission 上,否则加载期报 `InvalidDataException`。集合与显式 scoped 条目可同时出现,scope 归属显式条目。
- **校验**:未知集合标识、展开循环、集合成员未注册均由 `CapabilityLoader` / `CommandRouterComposer` 在启动期抛错,并列出可用集合标识。
- **组合期单次遍历**:`AddTaruiShell` 注册的 `CommandRouterBuilder` 单例同时供 `IPermissionSetCatalog` 与 `CommandRouter` 使用,保证「集合展开结果」与「路由实际注册的权限」来自同一次插件遍历。

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
    "window://destroyed", "shell://theme-changed",
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

### 3.5 窗口关闭

`core:window|close` 直接请求关闭窗口，无协作式拦截或回执通道。

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

> 注:运行时实际发出的错误码为**大写下划线**形式(如 `PERMISSION_DENIED`、`SCOPE_DENIED`、`COMMAND_NOT_FOUND`、`INVALID_ARGUMENTS`、`NOT_SUPPORTED`、`RUNTIME_TYPE_MISMATCH`、`COMMAND_FAILED`);`CommandRouter` 把 `NotSupportedException` 映射为 `NOT_SUPPORTED`,把 `TaruiRuntimeTypeMismatchException` 映射为 `RUNTIME_TYPE_MISMATCH`(请求了当前 webview 运行时不提供的运行时专属扩展,见 [`webview.md`](webview.md) §3)。上表沿用的早期小写记法仅表示同一语义,以运行时字符串与 `IpcCommandError.code` 为准。

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

`Window.getCurrent()` 无 label 时指向当前 Webview 所在窗口,`getByLabel('editor')` 寻址其它窗口。生命周期订阅(`onMoved` / `onResized` / `onFocusChanged` / `onDestroyed`)统一包装 `listen`;`Channel` 通过 `stream` 工具与原生 `plugin:fs|read-file-stream` 等命令对接。

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
