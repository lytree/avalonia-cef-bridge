# 插件使用

> 启用插件、Scope allow/deny、常见插件速查。
>
> 配套文档:[`../design/plugin-system.md`](../design/plugin-system.md)、[`./capabilities.md`](capabilities.md)。

## 1. 启用插件

每个 `Tarui.Plugins.*` 包提供一个 `Add*Plugin()` 扩展,在 `Program.cs` 显式注册:

```csharp
var builder = TaruiHost.CreateApplicationBuilder(args);
builder.Services
    .AddTaruiShell()
    .AddCefGlueWebView()
    .AddCorePlugin()
    .AddWindowPlugin()
    .AddWebviewPlugin()
    .AddWindowStatePlugin()
    .AddEventPlugin()
    .AddDialogPlugin()
    .AddSystemPlugin()
    .AddShellPlugin()
    .AddFileSystemPlugin()
    .AddMenuPlugin()
    .AddTrayPlugin()
    .AddNotificationPlugin()
    .AddAutostartPlugin()
    .AddGlobalShortcutPlugin()
    .AddStorePlugin()
    .AddLogPlugin()
    .AddHttpPlugin()
    .AddDeepLinkPlugin()
    .AddUpdaterPlugin()
    .AddCookiePlugin()
    .AddWebsocketPlugin()
    .AddPersistedScopePlugin()
    .AddPositionerPlugin();
```

注册后,命令 ID 出现在 `CommandRouter.RegisteredPermissions`,Capability 才能授权。

## 2. 常见插件速查

| 插件 | 命令前缀 | 关键命令 | 用途 |
| --- | --- | --- | --- |
| `Tarui.Plugins.Core` | `core:app|*` | `get-info` | 壳握手(产品、版本、能力列表) |
| `Tarui.Plugins.Window` | `core:window|*` | 24 条 | 窗口生命周期、几何、装饰、监视器 |
| `Tarui.Plugins.Webview` | `plugin:webview|*` | `navigate` / `get-state` / `devtools` / `eval` / `eval-with-callback` / `set-zoom` / `print` | 当前 Webview + 多 Webview 寻址 + DevTools + 脚本回执 |
| `Tarui.Plugins.WindowState` | `plugin:window-state|*` | `save` / `restore` / `clear` | 持久化窗口尺寸/位置(显示器拟合) |
| `Tarui.Plugins.Event` | `core:event|*` | `emit` | Web 发出事件(限于 `user://` 前缀) |
| `Tarui.Plugins.Dialog` | `plugin:dialog|*` | `open` / `save` / `message` / `confirm` / `ask` | 文件/目录/消息/确认/ask |
| `Tarui.Plugins.System` | `core:path`/`core:os`/`core:process`/`core:shell`/`core:clipboard`/`core:cli`/`core:channel`/`core:platform` | 路径解析、OS 信息、进程、shell open、剪贴板、CLI 解析、Channel、平台能力矩阵 | 系统能力 |
| `Tarui.Plugins.FileSystem` | `plugin:fs|*` | 16 条命令(含流式 + watch) | 受 `PathScope` 限制的文件系统 |
| `Tarui.Plugins.Menu` | `plugin:menu|*` | `set-window-menu` / `append` / `insert` / `remove` / `update-item` / `show-context-menu` | 原生菜单 + 上下文菜单 |
| `Tarui.Plugins.Tray` | `plugin:tray|*` | `create` / `set-menu` / `set-icon` / `set-tooltip` / `set-visible` / `remove` | 系统托盘 |
| `Tarui.Plugins.Notification` | `plugin:notification|*` | `permission-state` / `request-permission` / `show` / `cancel` | 系统通知 |
| `Tarui.Plugins.Autostart` | `plugin:autostart|*` | `is-enabled` / `enable` / `disable` | 开机自启(三平台) |
| `Tarui.Plugins.GlobalShortcut` | `plugin:global-shortcut|*` | `register` / `unregister` / `unregister-all` / `is-registered` | 系统级快捷键 |
| `Tarui.Plugins.Store` | `plugin:store|*` | `get` / `set` / `has` / `delete` / `clear` / `keys` | KV 持久化 |
| `Tarui.Plugins.Log` | `plugin:log|*` | `record` | 日志落盘 |
| `Tarui.Plugins.DeepLink` | `plugin:deep-link|*` | `get-current` / `feed` | 自定义 scheme 路由 |
| `Tarui.Plugins.Updater` | `plugin:updater|*` | `check` / `download` / `apply` | 应用更新(签名 + SHA-256 + MSIX 安装) |
| `Tarui.Plugins.Http` | `plugin:http|*` | `fetch` / `upload` | 受限 fetch / multipart 上传 |
| `Tarui.Plugins.Shell` | `plugin:shell|*` | `spawn` / `stdin` / `kill` | 子进程 + Channel 流式 stdio |
| `Tarui.Plugins.Cookie` | `plugin:cookie|*` | `list` / `set` / `remove` / `flush` | Cookie 管理 |
| `Tarui.Plugins.Websocket` | `plugin:websocket|*` | `connect` / `send` / `close` | WebSocket(Channel 帧推送) |
| `Tarui.Plugins.PersistedScope` | `plugin:persisted-scope|*` | `scope-allow` / `scope-deny` / `scope-reset` | 运行时 scope 扩展 |
| `Tarui.Plugins.Positioner` | `plugin:positioner|*` | `set-position` | 8 屏幕锚位 + 6 Tray* 锚位 |

完整插件清单与命令 ID 见 [`../design/plugin-system.md`](../design/plugin-system.md) §3。

## 3. Scope 配置

许多插件支持细化的 scope 配置(在 capability 文件中):

```jsonc
{
  "identifier": "plugin:fs|read-text-file",
  "allow": [
    { "base": "appData", "path": "documents/**" },
    { "base": "appConfig", "path": "**/*.json" },
    { "base": "resources" }
  ]
}
```

详见 [`./capabilities.md`](capabilities.md) §4。

## 4. 前端调用

每个插件对应一个 `@lytree/api/<plugin>` 子路径模块:

```ts
import { fs, store } from '@lytree/api/fs';
import { readDir } from '@lytree/api/fs';  // 函数式风格

await fs.writeTextFile({ path: 'documents/note.txt', contents: 'Hello' });
await store.set('settings', { theme: 'dark' });
```

详细见 [`./frontend-bridge.md`](frontend-bridge.md)。

## 5. 第三方插件接入流程

```powershell
# 1. 安装后端包
dotnet add package Tarui.Plugins.Foo

# 2. 注册
builder.Services.AddFooPlugin();

# 3. 安装前端包
pnpm add @lytree/plugin-foo

# 4. capability 授权
# capabilities/main.json 添加 "plugin:foo|do"

# 5. tarui build 自动合成校验 schema
```

详细见 [`../design/cli-workflow.md`](../design/cli-workflow.md) §W4。

## 6. Plugin 命令表参考

每个插件的精确命令 ID 与权限 ID 在其 README 与 Capability 文件中列出;权威命令清单见 `examples/demo/capabilities/main.json`(所有插件都被 demo 加载,所有命令 ID 与 scope 都在那里)。
