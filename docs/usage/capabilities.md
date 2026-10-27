# 能力清单:`capabilities/*.json`

> IPC 权限闸门。每个窗口/WebView 持有 capability,只允许执行显式列入的命令与事件。
>
> 配套文档:[`../design/ipc.md`](../design/ipc.md)、[`./plugins.md`](plugins.md)。

## 1. 缺省即拒绝

每个窗口/WebView 持有标识符(如 `main`),只允许执行其 capability 中显式列入的命令与事件。**缺省即拒绝**(无显式 capability = 拒绝一切)。

`CommandRouterComposer.Compose(...)` 在启动期校验:若 capability 文件中出现未注册权限(跳过 `"*"` 通配)即抛 `InvalidOperationException`。

## 2. 文件结构

`capabilities/main.json`:

```json
{
  "identifier": "main",
  "description": "Main window desktop permissions",
  "windows": ["main"],
  "platforms": ["windows", "macos", "linux"],
  "events": [
    "window://moved",
    "window://resized",
    "window://focus-changed",
    "window://destroyed",
    "shell://theme-changed",
    "tray://clicked",
    "notification://activated",
    "deeplink://tarui",
    "updater://status",
    "demo://echo"
  ],
  "permissions": [
    "core:app|get-info",
    "core:app|exit",
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

完整 schema 见 [`../../schemas/tarui-desktop-capability.schema.json`](../../schemas/tarui-desktop-capability.schema.json)。

## 3. 字段语义

| 字段 | 作用 |
| --- | --- |
| `identifier` | capability 唯一标识 |
| `description` | 可选,人读说明 |
| `windows` | 适用的窗口 label 列表(`["main"]`、`["editor"]`) |
| `platforms` | 平台白名单(`["windows", "macos", "linux"]`) |
| `events` | 允许 **接收** 的事件(原生 + 自定义) |
| `permissions` | 允许 **调用** 的命令:字符串=全权(或权限集合标识);对象=细化 scope |

## 4. `permissions` 形式

### 4.1 字符串(全权)

```jsonc
"permissions": [
  "core:app|get-info",
  "core:window|set-title"
]
```

### 4.2 对象(细化 scope)

```jsonc
"permissions": [
  {
    "identifier": "plugin:store|set",
    "allow": [
      { "base": "appData", "path": "settings.json" },
      { "base": "appConfig", "path": "**/*.json" }
    ],
    "deny": [
      { "base": "appConfig", "path": "settings/protected.json" }
    ]
  }
]
```

### 4.3 `base` 枚举

| `base` | 物理路径 | 适用 |
| --- | --- | --- |
| `appData` | `AppContext.BaseDirectory` / `LocalApplicationData/<id>/data/` | 用户文档、KV |
| `appConfig` | `LocalApplicationData/<id>/config/` | 应用配置 |
| `appCache` | `LocalApplicationData/<id>/cache/` | 缓存 |
| `appLog` | `LocalApplicationData/<id>/log/` | 日志 |
| `temp` | 系统临时目录 | 临时文件 |
| `resources` | 打包资源(只读) | 应用内置 |

`path` 走 glob(`.NET.FileSystemGlobbing`):`**` 多层、`*` 单层、`?` 单字符。

### 4.4 Scope 拒绝优先

`allow` 与 `deny` 同存,deny 命中即拒绝。这适用于 FileSystem / GlobalShortcut / Store / PersistedScope / Http 等插件。

```jsonc
{
  "identifier": "plugin:fs|remove",
  "allow": [
    { "base": "appData" },
    { "base": "temp" }
  ],
  "deny": [
    { "base": "appConfig", "path": "settings/protected.json" }
  ]
}
```

## 5. 权限集合(推荐写法)

capability 不必逐条罗列命令,可以直接引用**权限集合**。集合在加载期展开为具体权限,清单因此只描述「要哪些能力面」,不随插件新增命令而腐化。

```jsonc
"permissions": [
  "core:window|default",        // 本窗口的窗口控制:close / set-title / get-state …
  "core:window|other-window",   // 跨窗口操作:close-other-window …
  "plugin:sql|default",         // 该插件的全部标准权限
  "app|default",                // 应用自有命令(无 core:/plugin: 前缀)
  {
    "identifier": "core:window|create",
    "allow": [{ "path": "editor" }]
  }
]
```

命名规则:

| 集合 | 含义 |
| --- | --- |
| `{命名空间}\|default` | 该命名空间下除跨目标守卫外的全部权限。`plugin:fs|default`、`core:window|default` |
| `{命名空间}\|other-window` | `-other-window` 守卫权限(操作其他窗口) |
| `{命名空间}\|other-webview` | `-other-webview` 守卫权限(操作其他 WebView) |
| `app\|default` | 应用自有命令的隐式集合 |

`{命名空间}` 是权限 ID 最后一个 `|` 之前的部分:`plugin:fs|read-text-file` → `plugin:fs`。

三条硬规则:

1. **`|default` 不含跨目标守卫**。要操作别的窗口/WebView,必须额外声明 `|other-window` / `|other-webview`。
2. **集合不能带 `allow` / `deny`**。scope 属于单条权限;需要细化路径或 URL 时,把该权限单独写一条带 scope 的条目(集合与显式条目可以并存,scope 以显式条目为准)。
3. **敏感权限可能被插件排除在 `|default` 之外**。例:`plugin:webview|send-devtools-message`(CDP 通道)必须显式授予,`plugin:webview|default` 不含它。

启动期校验:未知集合标识、展开循环、集合成员未注册都会让 `CommandRouterComposer.Compose(...)` 抛 `InvalidOperationException`,并在消息里列出当前可用的集合标识。

## 6. `events` 形式

```jsonc
"events": [
  "window://moved",             // 窗口移动
  "window://resized",           // 窗口缩放
  "window://focus-changed",     // 焦点变化
  "window://destroyed",         // 窗口销毁
  "shell://theme-changed",      // 主题广播
  "tray://clicked",             // 托盘点击
  "notification://activated",   // 通知按钮激活
  "deeplink://tarui",           // DeepLink 事件
  "updater://status",           // 更新状态
  "demo://echo"                 // 用户自定义(user:// 前缀)
]
```

关键规则:

- **`user://*` 默认接收**(无原生数据);保留原生前缀必须显式列入。
- **`window://*`** 与 **`shell://theme-changed`** 由 shell 主动发出。
- 缺省即拒绝。

## 7. 多窗口隔离

`create-window` 时只有两个窗口的 ID / events / permissions 完全匹配时才允许;创建者上下文通过 `CommandContext.Capabilities` 做提权防护:

```jsonc
// capabilities/main.json
{
  "permissions": [
    {
      "identifier": "core:window|create",
      "allow": [{ "path": "editor" }]
    }
  ]
}

// capabilities/editor.json —— 必须显式存在
{
  "identifier": "editor",
  "windows": ["editor"],
  "platforms": ["windows", "macos", "linux"],
  "events": ["window://moved", "window://resized"],
  "permissions": ["core:window|set-title"]
}
```

主窗口通过 `core:window|create` 创建 editor 窗口时,shell 校验目标 capability `editor` 存在,且创建者有 `core:window|create` 权限。无提权路径。

## 8. 窗口关闭

`core:window|close` 直接请求关闭窗口，无协作式拦截或回执通道。

## 9. Persisted Scope(运行时权限扩展)

`plugin:persisted-scope|scope-allow` / `scope-deny` / `scope-reset` 在 `appData/scope.json` 中持久化运行时 scope 变更。`IRuntimeScopeOverlay` 在 IPC 层与静态 capability scope 合并,**deny 优先**。仅扩展已声明 scope 的权限。

```ts
import { allowScope, denyScope, resetScope } from '@lytree/api/persisted-scope';

await allowScope('plugin:fs|read-text-file', { base: 'appData', path: 'documents/**' });
await denyScope('plugin:http|fetch', { path: 'https://evil.example/**' });
await resetScope('plugin:fs|read-text-file'); // 清除持久化覆盖
```

## 10. 完整示例:多窗口应用

```text
capabilities/
  main.json    # 主窗口:全权 + 部分核心权限
  editor.json  # 编辑器窗口:窗口控制 + fs + updater
```

主窗口 `core:window|create` 显式 allow `editor` label;编辑器窗口必须显式存在 `editor.json`。
