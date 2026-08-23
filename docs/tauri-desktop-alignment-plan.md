# Tarui 桌面功能对齐 Tauri 实施步骤

> 状态：设计基线
> 基线日期：2026-08-21
> 对齐目标：Tauri v2 桌面能力与开发体验
> 本文只定义实施步骤、架构边界和验收门禁，不代表对应功能已经实现。

## 1. 目标与非目标

Tarui 的目标不是逐行复刻 Tauri 的 Rust 内部实现，而是在 .NET、Avalonia 和 CefGlue 技术栈上提供可迁移、可预测的桌面开发体验：

- 前端 API 的模块划分、方法语义和返回类型尽量与 Tauri v2 对齐。
- 权限、Capability、Scope、多窗口隔离和平台可用性具有等价安全语义。
- 原生能力继续通过显式插件注册，不引入程序集扫描、运行时反射或动态插件加载。
- DTO 继续使用 `JsonSerializerContext` 源码生成元数据。
- Avalonia 管理窗口和桌面 UI，Hosting 管理进程生命周期，插件只暴露明确契约。
- Windows、macOS、Linux 共用契约；允许按阶段先完成 Windows 实现，但不允许静默伪装为跨平台可用。

以下内容不属于近期对齐目标：

- 移动端 API。
- 复刻 Tauri 的 Rust resource table 内部结构。
- 兼容任意第三方 Tauri Rust 插件。
- 通过反射自动生成插件注册或 JSON 序列化回退。
- 在 IPC 尚未支持真实流式传输前处理无上限的大文件。

### 1.1 对齐状态定义

本文使用以下状态，避免把“存在同名 API”误判为已经对齐：

| 状态 | 含义 |
| --- | --- |
| 已对齐 | 前端语义、原生行为、权限边界和测试均满足目标 |
| 部分对齐 | 已有可用实现，但 API、权限、平台或生命周期仍有缺口 |
| 已规划 | 已在本文确定契约、阶段和验收要求，尚未实现 |
| 未开始 | 规划已经存在，但尚无对应代码或测试变更 |
| 进行中 | 已有实现变更，但尚未满足阶段退出条件 |
| 阻塞 | 存在明确外部依赖或技术阻断，并记录恢复条件 |
| 延后 | 不属于桌面核心阶段，等待前置能力或产品需求 |
| 未验证 | 可能已经存在实现，但尚无足够测试或运行证据证明 |

对齐完成必须同时具备代码、Capability、TypeScript API、测试和文档证据，不能只凭命令已注册判定。

## 2. 当前 Tarui 基线

当前仓库已经形成完整的桌面壳层，不应推倒重建：

| 能力域 | 当前状态 | 已有表面 |
| --- | --- | --- |
| App | 已实现 | 应用握手、版本、能力列表 |
| Window | 已实现 | 创建、多窗口、尺寸、位置、显隐、焦点、置顶、全屏、显示器 |
| Event | 已实现 | Web 发出事件、窗口定向和广播、原生窗口事件 |
| Dialog | 已实现 | 打开文件、打开目录、保存文件 |
| Clipboard | 已实现 | 文本读取和写入 |
| OS / Path | 已实现 | 操作系统信息、常用目录解析 |
| Process | 已实现 | 退出、重新启动 |
| Opener / Shell | 已实现 | 使用系统默认程序打开 URL 或绝对路径 |
| Capability | 部分实现 | 按窗口的命令字符串 allow-list |
| Channel | 仅有类型骨架 | .NET 与 TypeScript 各自存在类型，尚未形成端到端流式协议 |
| File System | 未实现 | 只有路径解析和系统文件选择器 |
| Menu / Tray | 未实现 | Avalonia 提供菜单和托盘原语，但 Tarui 尚未接入或封装 |
| Single Instance | 未实现 | 尚无主实例协调和参数转发 |
| Notification | 未实现 | 尚无系统通知抽象 |
| Autostart | 未实现 | 尚无平台启动项管理 |
| Global Shortcut | 未实现 | 尚无系统级快捷键注册 |
| Window State | 未实现 | 尚无窗口位置和状态持久化 |
| Deep Link | 已实现(Windows) | Windows cold/warm 全链路已接；macOS delegate / Linux .desktop 待真机验收 |
| Updater | check/download 已实现(Windows) | §10.6：签名清单 + 逐文件哈希校验 + 受控 staging + `updater://status` 事件已接；apply 仍默认关闭（前置未就绪） |
| File Drop / Drag Region | 未实现 | 当前 Avalonia 12 windowed CEF 适配未暴露对应事件 |

现有 5 个插件共注册 35 个权限匹配命令。新增能力必须继续沿用以下链路：

```text
Tarui.Contracts DTO
  -> ITaruiPlugin 显式注册
  -> Shell/Hosting/平台服务实现
  -> Capability 授权
  -> @lytree/api 类型化模块
  -> 控制台式自测试
```

### 2.1 Tauri v2 到 Tarui 的模块映射

| Tauri v2 表面 | Tarui 当前表面 | 状态 | 目标阶段 |
| --- | --- | --- | --- |
| `@tauri-apps/api/core` invoke / Channel | `@lytree/api/ipc` | 部分对齐：invoke 可用，Channel 未接线 | Phase 0 / 后续 Channel 专项 |
| `@tauri-apps/api/app` | `@lytree/api/app` | 部分对齐：握手可用，产品信息硬编码 | Phase 0 |
| `@tauri-apps/api/window` | `@lytree/api/window` | 部分对齐：主要窗口控制已实现 | Phase 0 / Phase 5 |
| `@tauri-apps/api/webviewWindow` | 合并在 Window 与 WebViewHost | 已规划：暂不强制拆成独立对象 | Phase 5 |
| `@tauri-apps/api/event` | `@lytree/api/event` | 部分对齐：缺少系统事件保护和接收权限 | Phase 0 |
| `@tauri-apps/api/path` | `@lytree/api/path` | 部分对齐：目录解析可用，路径边界需加固 | Phase 0 |
| Dialog plugin | `@lytree/api/dialog` | 部分对齐：文件、目录和保存选择可用 | Phase 1 会话 grant |
| Opener plugin | `@lytree/api/shell` | 部分对齐：默认程序打开可用，缺少 scope | Phase 0 |
| Clipboard Manager plugin | `@lytree/api/clipboard` | 部分对齐：仅文本 | 后续增强 |
| OS plugin | `@lytree/api/os` | 部分对齐：基础系统信息可用 | Phase 0 功能矩阵 |
| Process plugin | `@lytree/api/process` | 部分对齐：退出和重启需纳入 Host 生命周期 | Phase 0 |
| File System plugin | `@lytree/api/fs` | 部分对齐：九条命令、scoped allow/deny、glob 匹配、resources 只读、原子写、大小预算 | Phase 1 会话 grant + Channel 流式传输 |
| Menu API | 无 | 已规划 | Phase 2 |
| Tray Icon API | 无 | 已规划 | Phase 2 |
| Single Instance plugin | 无 | 已规划 | Phase 3 |
| Window State plugin | 无 | 已规划 | Phase 3 |
| Notification plugin | 无 | 已规划 | Phase 4 |
| Autostart plugin | 无 | 已规划 | Phase 4 |
| Global Shortcut plugin | 无 | 已规划 | Phase 4 |
| Drag/drop 与 draggable region | 无 | 已规划 | Phase 5 |
| Store / Log / Deep Link / Updater | 无 | 已规划 | Phase 6 |
| HTTP / SQL / Stronghold 等可选插件 | 无 | 延后 | 产品需求驱动 |

### 2.2 对齐交付物映射

| 能力 | TypeScript 表面 | C# 所有权 | IPC / 事件 | Capability 与验证证据 |
| --- | --- | --- | --- | --- |
| App / Core | `app.ts`、`ipc.ts` | `Tarui.Contracts`、`Tarui.Ipc`、`Tarui.Plugins.Core` | `core:app|*` | 权限 ID、握手快照、错误码测试 |
| Window | `window.ts` | `Tarui.Plugins.Window`、`Tarui.Shell` | `core:window|*`、`window://*` | current/other-window 权限、窗口集成测试 |
| Event | `event.ts` | `Tarui.Plugins.Event`、`EventRouter` | `core:event|emit`、`user://*` | 发送权限、接收事件列表、伪造拒绝测试 |
| Dialog grant | `dialog.ts` | `Tarui.Plugins.Dialog`、`AvaloniaDialogService` | `plugin:dialog|*` | 调用窗口绑定、会话 grant 测试 |
| File System | `fs.ts` | `Tarui.Plugins.FileSystem`、`Tarui.Ipc/FileAccessPolicy` | `plugin:fs|*` | allow/deny path scope、glob(`**/*`/`/*`)、链接和大小测试、9 条命令覆盖测试 |
| Menu | `menu.ts` | `Tarui.Plugins.Menu`、Avalonia menu service | `plugin:menu|*`、`menu://*` | owner window、资源释放和点击事件测试 |
| Tray | `tray.ts` | `Tarui.Plugins.Tray`、Avalonia tray service | `plugin:tray|*`、`tray://*` | owner/app resource、Host 退出清理测试 |
| Single Instance | `single-instance.ts` 仅事件 helper | `Tarui.SingleInstance` | `app://second-instance` | 事件接收权限、真实双进程测试 |
| Window State | `window-state.ts` | `Tarui.Plugins.WindowState` | `plugin:window-state|*` | 应用目录 scope、多显示器恢复测试 |
| Notification | `notification.ts` | `Tarui.Plugins.Notification` | `plugin:notification|*`、`notification://*` | query/show/cancel 权限、系统授权测试 |
| Autostart | `autostart.ts` | `Tarui.Plugins.Autostart` | `plugin:autostart|*` | query/modify 分离、安装路径测试 |
| Global Shortcut | `global-shortcut.ts` | `Tarui.Plugins.GlobalShortcut` | `plugin:global-shortcut|*`、对应事件 | accelerator scope、冲突和释放测试 |
| WebView Desktop | `window.ts` 或后续 `webview.ts` | `Tarui.WebView.Abstractions`、CefGlue adapter | `window://file-*`、`webview://*` | 事件权限、真实拖放和导航测试 |

每一行只有在以下证据齐全后才可标记“已对齐”：C# 契约快照、TypeScript 类型测试、Capability 拒绝测试、目标平台集成测试、已知差异记录。

## 3. 对齐原则

### 3.1 对齐语义，不强制对齐内部命令名

现有 `core:window|set-title` 等命令名已经形成线上协议。默认保持兼容，在 TypeScript 层提供接近 Tauri 的模块和方法：

```ts
import { getCurrentWindow } from '@lytree/api/window'
import { readTextFile } from '@lytree/api/fs'
import { TrayIcon } from '@lytree/api/tray'
```

只有在旧命令存在安全问题或无法表达新契约时才新增命令，不批量重命名现有协议。

### 3.2 核心能力和插件能力分离

- `core:*`：保持现有 App、Window、WebView、Event 和 IPC 壳层原语。
- `plugin:*`：文件系统、菜单、托盘、通知、自动启动、全局快捷键和更新器等可独立注册能力。
- Hosting 基础设施：必须早于窗口创建或贯穿进程生命周期的能力，例如单实例协调。

Tarui 可以保留当前命名以避免破坏兼容，但项目所有权必须按上述边界组织。

### 3.3 功能可用性和权限授权分开

权限获批不代表当前平台一定支持该能力。`AppHandshake` 后续应增加平台功能矩阵：

```csharp
public sealed record FeatureAvailability(
    string Feature,
    bool Available,
    string? Reason = null);
```

前端可据此区分：

- 没有权限：`PERMISSION_DENIED`
- 当前平台不支持：`NOT_SUPPORTED`
- 系统占用或冲突：`RESOURCE_CONFLICT`
- 用户拒绝系统授权：`USER_DENIED`

### 3.4 API 兼容策略

Tarui 对齐优先级从高到低为：模块路径、方法语义、参数和结果结构、错误行为、对象外观、内部命令名。

1. 优先提供与 Tauri 熟悉用法接近的独立 TypeScript 子路径，例如 `@lytree/api/fs` 和 `@lytree/api/tray`。
2. 能直接复用的方法名、参数名和 camelCase 结果字段保持一致。
3. 现有 Tarui 方法不得为追求表面一致而无迁移期破坏；必要时保留旧导出并增加新别名。
4. Tauri 使用 resource object、Tarui 暂时使用声明式服务时，可以在 TypeScript 层提供对象 facade，但原生资源必须具有稳定 ID、owner 和显式释放语义。
5. 平台不支持、权限拒绝和参数错误必须通过稳定错误码表达，不能返回伪成功或依赖错误文本判断。
6. 内部命令名是 Tarui 线协议，不要求与 Tauri Rust command 一致。
7. 每个模块维护一张 API 差异表；差异消失后才能把状态从“部分对齐”改为“已对齐”。

## 4. Phase 0：安全与生命周期前置

任何文件系统、托盘、快捷键、通知功能开始前，必须完成本阶段。

### 4.1 Capability v2

当前权限只表示“窗口能否调用命令”，无法限制路径、菜单、托盘或快捷键资源。升级后同时支持字符串权限和结构化权限：

```json
{
  "$schema": "../schemas/tarui-desktop-capability.schema.json",
  "identifier": "main",
  "description": "Main window desktop permissions",
  "windows": ["main"],
  "platforms": ["windows", "macos", "linux"],
  "permissions": [
    "core:window|default",
    {
      "identifier": "plugin:fs|read-text",
      "allow": [
        { "base": "appData", "path": "documents/**/*.json" }
      ],
      "deny": [
        { "base": "appData", "path": "documents/private/**" }
      ]
    }
  ],
  "events": [
    "app://second-instance",
    "menu://item-clicked",
    "tray://clicked"
  ]
}
```

示例中的 `core:window|default` 是计划中的权限集合标识，不是一个可调用命令。权限集合必须在构建期展开并校验，运行时仍执行具体命令授权。

实施步骤：

1. 在 `Tarui.Contracts` 增加 Capability manifest DTO 和源码生成元数据。
2. 将 `CapabilitySet` 从字符串集合升级为权限 ID、allow scope、deny scope 和事件集合。
3. `CommandRouterBuilder.Add` 增加可选的类型化参数授权器。
4. Router 流程改为：查找命令 -> 反序列化 DTO -> 检查命令权限 -> 检查 scope -> 执行 handler。
5. deny scope 优先于 allow scope。
6. `CapabilityLoader` 对未知字段、重复 identifier、非法平台和非法 scope 启动失败。
7. 生成 JSON Schema，并将 schema 复制到应用输出或文档目录。
8. 生产 Capability 禁止使用 `"*"`；通配放行只保留给隔离测试环境。

### 4.2 显式窗口权限配置

当前没有专属 Capability 的窗口会回退到 `main`。高权限功能加入后必须取消该行为。

实施步骤：

1. 原生窗口配置增加可选 `Capability` profile；Web 请求中的窗口参数不能自由选择任意 profile。
2. `main` 继续固定解析主 Capability。
3. 动态窗口通过创建命令的 scope 映射到允许创建的 profile。
4. 创建者不能选择权限高于自身的 profile。
5. 未指定且没有精确窗口匹配时，以 `CAPABILITY_NOT_FOUND` 拒绝创建。
6. 跨窗口操作增加 `*-other-window` 权限，不再由普通当前窗口权限隐式覆盖。

### 4.3 系统事件保护

Web 自定义事件只允许使用 `user://` 命名空间。以下前缀由原生端保留：

```text
app://
window://
webview://
shell://
menu://
tray://
notification://
global-shortcut://
fs://
updater://
log://
```

`EventRouter` 在向窗口投递带敏感信息的原生事件前必须检查事件接收权限。第二实例参数、文件路径和通知动作不能广播给未授权窗口。

### 4.4 路径安全

所有文件能力统一通过一个 `IFileAccessPolicy`，禁止插件自行拼接路径：

1. 使用 `Path.GetRelativePath` 判断目标是否仍位于授权根目录。
2. 拒绝 rooted relative path、设备路径、控制字符和非法路径段。
3. Windows 逐段拒绝 reparse point 越界。
4. Linux/macOS 逐段处理符号链接解析后的真实路径。
5. 写入使用临时文件和原子替换，避免中途失败破坏原文件。
6. 所有读写命令具有单次大小上限和可配置总量上限。

### 4.5 Host 与窗口生命周期

1. `process.exit` 改为通过 `IHostApplicationLifetime.StopApplication()` 优雅退出。
2. relaunch 只在新进程启动成功后请求当前 Host 停止。
3. 窗口关闭时显式释放 `WebViewHost` 和浏览器资源。
4. 托盘、快捷键、监听器和单实例服务全部实现 `IDisposable` 或 `IHostedService.StopAsync`。
5. 增加 `Tarui:Application:ShutdownMode`：`OnMainWindowClose`、`OnLastWindowClose`、`Explicit`。

### 4.6 Phase 0 验收

- 旧字符串 Capability 文件仍可加载。
- 未授权窗口无法继承 `main` 的敏感权限。
- 低权限窗口不能通过创建高权限 label 或 profile 提权。
- Web 无法伪造原生事件。
- 路径逃逸、符号链接和 reparse point 测试全部失败关闭。
- 关闭 100 次动态窗口后 WindowRegistry、WebView 和事件订阅不残留。
- Host 停止时所有桌面资源按顺序释放。

## 5. Phase 1：受限文件系统

新增项目：

```text
src/plugins/Tarui.Plugins.FileSystem/
web/packages/api/fs.ts
tests/Tarui.FileSystem.Tests/
```

首期命令：

```text
plugin:fs|read-text-file
plugin:fs|write-text-file
plugin:fs|read-dir
plugin:fs|stat
plugin:fs|exists
plugin:fs|mkdir
plugin:fs|copy-file
plugin:fs|rename
plugin:fs|remove
```

前端 API 对齐目标：

```ts
readTextFile(path, options)
writeTextFile(path, contents, options)
readDir(path, options)
stat(path, options)
exists(path, options)
mkdir(path, options)
copyFile(from, to, options)
rename(oldPath, newPath, options)
remove(path, options)
```

限制：

- 首期只允许 `appData`、`appLocalData`、`appConfig`、`appCache`、`appLog`、`temp` 和只读 `resources`。
- 用户通过对话框选择的任意路径采用会话 grant，不允许仅凭字符串绝对路径绕过 scope。
- `resources` 永远只读。
- 默认文本文件上限 8 MiB，可配置但必须有硬上限。
- 二进制文件和大文件延后到 Channel 端到端协议完成后。
- watcher 独立为后续命令，不与基础读写一起交付。

验收：

- 每个命令具有独立权限。
- allow/deny scope 均有正反测试。
- Windows 大小写、UNC、设备路径、reparse point 覆盖测试。
- Linux/macOS 符号链接和权限错误返回稳定错误码。
- TypeScript 参数和结果与 C# DTO 一一对应。

## 6. Phase 2：原生菜单与托盘

新增项目：

```text
src/plugins/Tarui.Plugins.Menu/
src/plugins/Tarui.Plugins.Tray/
web/packages/api/menu.ts
web/packages/api/tray.ts
```

### 6.1 菜单

首期使用声明式菜单定义，不立即复制 Tauri resource table：

```text
plugin:menu|set-window-menu
plugin:menu|update-item
plugin:menu|remove-window-menu
```

事件：

```text
menu://item-clicked
```

菜单项 DTO 必须包含稳定 `id`，支持普通项、分隔线、子菜单、复选项、启用状态和快捷键显示。默认只能管理调用窗口的菜单，跨窗口管理需要额外权限。

### 6.2 托盘

命令：

```text
plugin:tray|create
plugin:tray|set-menu
plugin:tray|set-icon
plugin:tray|set-tooltip
plugin:tray|set-visible
plugin:tray|remove
```

事件：

```text
tray://clicked
tray://menu-item-clicked
```

托盘资源由创建窗口拥有；窗口销毁时按配置选择自动释放或转移给应用级 owner。托盘模式必须配合 `Explicit` ShutdownMode，菜单中的退出动作走 Host 正常停止流程。

验收：

- Windows、macOS、Linux 至少完成创建、菜单点击、显隐和释放。
- 图标缺失、格式不支持和重复 ID 返回明确错误。
- 多窗口不能修改其他窗口拥有的菜单或托盘资源。
- Host 退出后系统托盘不残留。

## 7. Phase 3：单实例与窗口状态

### 7.1 单实例

新增 `src/desktop/Tarui.SingleInstance`，它属于启动基础设施，不是普通 Web 命令插件。

启动顺序必须保持：

```text
CefGlueNextAvaloniaRuntime.RunSubProcess(args)
  -> 单实例主进程判定
  -> Tarui Host 构建和启动
  -> Avalonia 窗口创建
```

第二实例把以下信息发送给主实例后退出：

```json
{
  "arguments": [],
  "workingDirectory": "...",
  "timestamp": "..."
}
```

主窗口未注册时进入有界队列，注册后发送 `app://second-instance`。Windows 使用命名 Mutex 加 Named Pipe；macOS/Linux 使用进程锁加本地 socket，并保证通信端点只对当前用户开放。应用关闭时先等待所有 WebView native close 完成，再让 Avalonia loop 与 Host Stop/Dispose 完成，最后由 `Program` 的 `finally` 执行 CEF runtime shutdown。

### 7.2 窗口状态

新增 `Tarui.Plugins.WindowState`：

```text
plugin:window-state|save
plugin:window-state|restore
plugin:window-state|clear
```

保存位置、尺寸、最大化和全屏状态。恢复时必须根据当前显示器集合修正越界位置，不能把窗口恢复到已拔出的显示器上。

验收：

- 两个真实进程并发启动时只有一个主实例。
- 第二实例参数只投递给有事件权限的窗口。
- 主窗口未就绪时参数不丢失。
- 显示器变化后窗口仍可见。

## 8. Phase 4：通知、自动启动和全局快捷键

### 8.1 通知

```text
plugin:notification|permission-state
plugin:notification|request-permission
plugin:notification|show
plugin:notification|cancel
```

事件：`notification://activated`、`notification://dismissed`。

Windows 通知需要稳定应用身份；macOS 需要系统授权；Linux 依赖桌面通知服务。平台不支持动作按钮时必须通过功能矩阵声明，不得假装成功。

### 8.2 自动启动

```text
plugin:autostart|is-enabled
plugin:autostart|enable
plugin:autostart|disable
```

Web 端只能注册当前应用及预配置参数，不能提交任意 executable。安装路径变化、参数引用和卸载清理必须纳入测试。

### 8.3 全局快捷键

```text
plugin:global-shortcut|register
plugin:global-shortcut|unregister
plugin:global-shortcut|unregister-all
plugin:global-shortcut|is-registered
```

事件：`global-shortcut://triggered`。

权限 scope 可限制可注册的 accelerator。Wayland 等不完整平台返回 `NOT_SUPPORTED` 或明确的降级原因，不退化为仅窗口内快捷键。

Phase 4 验收：

- 通知权限查询、请求、展示、激活和取消在支持平台有真实运行证据。
- 自动启动只注册当前应用，启用、查询、禁用和卸载清理形成闭环。
- 快捷键冲突、平台保留键、重复注册和窗口/Host 释放均返回稳定结果。
- 每个平台分别记录完整支持、部分支持或 `NOT_SUPPORTED`，不使用统一成功结果掩盖差异。

## 9. Phase 5：WebView 深度桌面集成

本阶段必须修改 `Tarui.WebView.Abstractions` 和 CefGlue 适配层，不能伪装成普通 JSON 命令。

能力：

- 文件拖入进入、离开、放下事件。
- 文件路径和普通文本 payload。
- CSS `-webkit-app-region: drag` 风格的标题栏拖拽区域。
- WebView 下载、打开新窗口和导航策略。

事件：

```text
window://file-drop-entered
window://file-drop-left
window://file-dropped
webview://download-requested
webview://navigation-requested
```

必须先给 `ITaruiWebView` 增加类型化原生事件，再由 `ShellWindowFactory` 转成带窗口上下文的事件。拖拽区域需要原始指针信息，不通过一次性 IPC 调用 `BeginMoveDrag`。

Phase 5 验收：

- 使用真实 windowed CEF 验证文件进入、离开和放下，不以模拟 EventRouter 调用代替。
- 多窗口拖放只投递给命中的窗口，未授权窗口不接收文件路径。
- draggable/no-drag 区域可动态更新，交互控件不会误触发窗口移动。
- 下载和导航策略能够允许、拒绝或交给外部程序，并覆盖恶意 URL 输入。
- 反复创建和销毁 WebView 后无残留浏览器、事件处理器或原生拖放资源。

## 10. Phase 6：产品化插件

在桌面核心稳定后依次评估：

| 能力 | 建议项目 | 优先级 |
| --- | --- | --- |
| Store | `Tarui.Plugins.Store` | 高，提供轻量配置持久化 |
| Logging | `Tarui.Plugins.Log` | 高，与 Microsoft.Extensions.Logging 汇合 |
| Deep Link | `Tarui.Plugins.DeepLink` | 高，复用单实例激活通道 |
| Updater | `Tarui.Plugins.Updater` | 高，要求签名验证和原子替换 |
| HTTP Client | `Tarui.Plugins.Http` | 中，仅在浏览器 CORS 无法满足时提供 |
| SQL | `Tarui.Plugins.Sql` | 中，保持显式驱动注册 |
| Stronghold 等安全存储 | 独立安全插件 | 中，先定义威胁模型 |
| Upload / WebSocket | 独立网络插件 | 低，浏览器已有能力时避免重复 |

Updater 在没有签名校验、回滚和安装器策略前不得交付“检查到更新即执行”的半成品。

Phase 6 是滚动阶段，每个产品化插件必须建立独立工作项、威胁模型、平台矩阵和退出标准。单个插件完成不能使其余未选择插件自动获得“已对齐”状态。

### 10.1 Store 威胁模型与平台矩阵

Store 的职责是轻量 JSON 配置持久化，不作为安全凭据仓库。承载风险集中在对配置文件的越界读写与跨窗口越权。

| 威胁 | 缓解措施 | 测试编号 |
| --- | --- | --- |
| Web 通过任意 base/path 读写宿主文件 | 所有磁盘访问复用 `IFileAccessPolicy`：只允许 `appData`/`appLocalData`/`appConfig`/`appCache`/`appLog`/`temp`，拒绝 rooted/UNC/设备/控制字符/非法段/符号链接越界，拒绝读 `APP_BINARY` 等非白名单 base | 6.7 Scope 正反 + `resources` 写拒绝 |
| 借 `resources`（只读）路径写入 | `resources` 永远只读，写命令经 `StoreScopeAuthorizer` deny | 6.7 `ResourcesBaseRejectsWritesAsync` |
| 配置 scope 授权缺失导致越权 | 命令级权限 + `StoreScopeAuthorizer` 按 capability allow/deny scope 匹配（glob、`**`、deny 优先），未授权返回 `PERMISSION_DENIED` | 6.7 `ScopeAuthorizerRespectsAllowDenyAndWildcards` |
| 写入中断破坏配置 | `WriteAllBytesAtomicAsync` 临时文件 + 原子替换 | 6.3 实现 / FileAccessPolicy 原子写测试 |
| 值语义歧义（null 读 vs 缺失） | Tauri erase 语义：`null` value 删除 key，`StoreGetResult.Value` 为 `string?` | 6.7 `NullValueRemovesKeyAsync` |
| 明文凭据误存 | Store 仅存储字符串值，文档标注不作为安全凭据库；高敏数据走 Stronghold 等安全插件（延后评估） | 文档约束 |

平台可用性：Store 基于 .NET 文件 IO 与 `IFileAccessPolicy`，无平台专用 API，三个平台契约语义一致。Windows 已验证；macOS/Linux 待实机运行验证（契约与措辞在 build 门禁覆盖，期望行为一致，但未以此代替运行证据）。

### 10.2 Logging 威胁模型与平台矩阵

Logging 把渲染进程日志并入宿主 `Microsoft.Extensions.Logging` 管道，并把宿主日志以 `log://entry` 事件下发窗口。

| 威胁 | 缓解措施 | 测试编号 |
| --- | --- | --- |
| 渲染进程伪造任意 level/类别污染日志 | 未知 level 降级 `Information`，category 缺省 `renderer`；消息按字面转发，不当作结构化模板（避免 CA2254/日志注入） | 6.7 `UnknownLevelDegradesToInformationAsync` |
| 日志消息被当作日志模板解析 | `logger.Log(level, 0, message, null, (_,_) => message)` 用显式 formatter 闭包，用户消息不进入模板 | 6.7 转发测试 |
| 未授权窗口窃听宿主机敏感日志 | `log://entry` 加入保留前缀，`RemoteLogSink` 经 `EventRouter` 按 capability `events` 授权投递，未声明接收权限的窗口不接收 | 6.4 实现 / 事件授权机制 |
| `LogLevel.None` 误入管道 | `RemoteLogger` 过滤 `None`，不产生 `log://entry` | 6.7 `RemoteLoggerFiltersOutNoneLevel` |
| 日志循环放大 | 渲染记录单向上行并入日志管道；桌面日志单向下发窗口，高层 provider 不再回灌 IPC，避免有界 Channel 无限积压（`RemoteLogSink` 用无界 Channel + 后台排空） | 6.4 实现 |
| 高限频拖垮事件系统 | 未来可加采样/节流，当前保持简单 FIFO | 设计备注 |

平台可用性：Logging 基于 `Microsoft.Extensions.Logging` 与事件路由，无平台专用 API，Windows 已验证；macOS/Linux 待实机运行验证，契约语义一致。

### 10.3 Deep Link 评估（工作项）

**目标定义**：捕获以注册自定义协议（如 `tarui://`）启动应用的 URL；在应用已运行时把新到达的 URL 交给主实例；当前启动 URL 可通过命令查询。

**候选项目**：`Tarui.Plugins.DeepLink` + `Tarui.Shell/DeepLinkService`（事件桥）+ `Tarui.SingleInstance` 增强。

**复用点与界限**：Deep Link 与 SingleInstance 共享“命令行 argv 携带激活意图”这一事实，但职责不同：

- 复用：Windows/Linux 上，应用已运行时的链接在 OS 层启动一个携带该 URL 的新进程，这正好走 `SingleInstanceGuard.Acquire` 的次实例转发路径，URL 作为 argv 到达主实例。规划让 `SingleInstanceCoordinator` 把收到的 `SecondInstanceArgs` 同步通知给一个原生观察者（`ISecondActivationSink`），DeepLinkService 从中提取 URL。冷启动（无实例运行）的 URL 由主进程自身的启动 argv 捕获。
- 界限：SingleInstance 负责“是否主实例 + 跨进程转发 + `app://second-instance`”，Deep Link 负责“从 argv/通道解读 URL + `deeplink://*` 事件 + get-current”。不得让 Deep Link 接管实例锁或转发管道本身。
- **平台边界修正**：macOS 热链接（应用已在运行时）不走 argv，而是经 AppKit `application(_:openURLs:)` 委托直接派发给已运行实例。故 macOS 需要在 Darwin 层挂一个 delegate 桥，把 URL 调用到 `ISecondActivationSink`/DeepLinkService，不能只依赖单实例足参数通道。Windows/Linux 冷热链接均沿 argv 路径，可完整复用单实例通道。

**契约草稿**（注册 `TaruiJsonContext`）：

```csharp
public sealed record DeepLinkCurrentResult(string? Url);
// 事件 payload：原始 url 字符串
```

**命令与事件**：

```text
plugin:deep-link|get-current     // 返回 DeepLinkCurrentResult
事件：deeplink://<scheme>          // scheme ∈ 已注册协议集合
```

事件名按 scheme 生成（对齐 Tauri `deeplink://site`）。由于事件授权是精确匹配且生产禁用 `*`，capability 必须按已注册 scheme 显式列出 `deeplink://<scheme>`；scheme 集合来自配置 `Tarui:Application:DeepLinkSchemes`。

**威胁模型**

| 威胁 | 缓解措施 |
| --- | --- |
| 协议占用/被劫持，URL 未被本应用接收 | 仅注册显式 scheme；Windows 写入 `HKCU\Software\Classes\`，校验 ShellOpen 命令解析到本应用 exe 路径；linux `.desktop` 声明 `x-scheme-handler/<scheme>`；macOS `CFBundleURLTypes`。多实例并行注册时以最后写入为准并诚实登记 |
| 伪造/畸形 URL（控制字符、超长、CRLF）注入日志或被当作命令 | DeepLinkService 校验 scheme 属于已注册集合，拒绝控制字符与超长（上限入契约）；URL 仅作为数据上报 Web，不在原生端执行任何动作 |
| 未授权窗口窃听链接负载 | `deeplink://` 加入 `EventNames` 保留前缀；`EventRouter` 按 capability `events` 精确授权投递 |
| 次实例转发投递的 URL 被伪造 | 沿用单实例通道现有的同用户隔离（Windows 命名管道 / Unix 域 socket）；在既有信任边界内传递，不新增更广暴露面 |
| 链接触发敏感 Web 动作（钓鱼意图） | 原生端只转发 URL 数据，不解析为跳转/命令；是否放行由前端策略与用户意图决定，需在示例中展示确认路径 |
| cold 与 warm 双路径漏发或重复 | 单一 `DeepLinkService` 消费统一 URL 流（启动 argv 播种 + 观察者转发），main 窗口就绪后 `Flush()` 补偿 warm 前到达的 URL，与 `app://second-instance` 的队列语义对齐 |

**平台矩阵**

| 平台 | 协议注册 | cold 链接（未运行） | warm 链接（已运行） | 验证状态 |
| --- | --- | --- | --- | --- |
| Windows | `HKCU\Software\Classes\<scheme>` | 启动 argv → get-current | 新进程 argv → 走单实例转发 → 观察者 | 已完成(Windows)/已验证 |
| macOS | `CFBundleURLTypes`(Info.plist) | 启动 argv | AppKit `openURLs` delegate 桥（不经 argv）| 待实现/未验证 |
| Linux | `.desktop` `x-scheme-handler/<scheme>`（`~/.local/share/applications`，`xdg-mime default`）| 启动 argv（平台无关，已实现）| 新进程 argv → 单实例转发 → 观察者 | 内容生成已单测；xdg/真机待验 |

**退出标准**

- `plugin:deep-link|get-current` 具有独立权限，未授权拒绝。
- cold/warm 两条路径都被投递（Windows 冷启动 argv、次实例转发 argv、macOS delegate 各一条）。
- URL 含控制字符/超长/非注册 scheme 被拒绝，且不产生 `deeplink://*` 事件。
- `deeplink://<scheme>` 为保留前缀，未在 capability 声明的窗口不接收。
- 示例应用能够展示收到、拒绝与非支持状态。
- 平台矩阵记录真实验证结果；macOS delegate 桥未完成前不得标记跨平台可用。
- 前置阶段门禁全部通过。

**已定决策**：

- 事件模型：按 scheme 生成 `deeplink://<scheme>` 事件（对齐 Tauri `deeplink://site`），避免跨协议串扰，capability 按已注册 scheme 显式授权。
- 本轮范围：实现 Windows/Linux 全链路（注册 + cold/warm）+ macOS 委托桥骨架（Darwin delegate 转发到 `ISecondActivationSink`，真机仅 Windows 验证）。
- 协议集合：来自配置 `Tarui:Application:DeepLinkSchemes`（JSON 字符串数组），运行时加载并在启动期校验。非空数组才注册/播种。

**实现与验收记录**：

- 契约：`Tarui.Contracts/DeepLinkContracts.cs` 增 `DeepLinkCurrentResult`、`DeepLinkFeedOptions`，并注册 `TaruiJsonContext`；`EventNames.ReservedPrefixes` 增 `deeplink://`。
- 单实例：`ISecondActivationSink` 观察者接口；`SingleInstanceCoordinator.Receive/Flush` 均同步通知注册的 sink（尽力而为、不抛错）。
- 插件：`Tarui.Plugins.DeepLink` 暴露 `IDeepLinkService`；`DeepLinkPlugin` 注册 `plugin:deep-link|get-current`、`plugin:deep-link|feed` 两命令与两权限；`AddDeepLinkPlugin()` 组合根注册。
- Shell：`DeepLinkUri`（scheme 校验 + URL 提取，拒绝控制字符/超长）、`DeepLinkConfiguration`（读取 `Tarui:Application:DeepLinkSchemes` 并去重/过滤非法）、`DeepLinkService`（cold argv 播种 + warm 观察者 + `Deliver` 发 `deeplink://<scheme>` 事件 + get-current/feed）、`WindowsDeepLinkRegistrar`（`HKCU\Software\Classes` 每用户注册）、`DeepLinkRegistrarHostedService`。
- 接线：`Program.cs` 增 `AddDeepLinkPlugin()`；`appsettings.json` 配 `Tarui:Application:DeepLinkSchemes: ["tarui"]`；`capabilities/main.json` 授权 `plugin:deep-link|*` 与 `deeplink://tarui` 事件。
- 跨平台：新增 `LinuxDeepLinkRegistrar`（`~/.local/share/applications/tarui.net/<scheme>.desktop` 声明 `x-scheme-handler/<scheme>`，`Exec="<exe>" %u`，best-effort `xdg-mime default`），接入 `DeepLinkRegistrarHostedService`；`.desktop` 内容生成已加单测。cold 播种与 warm 观察者路径平台无关，Linux/macOS 复用同一 `DeepLinkService`。
- Web API：`web/packages/api/deep-link.ts`（`getCurrent`/`feed`/`onDeepLink`/`deepLink`）+ `index.ts` barrel（`getCurrentDeepLink`/`feedDeepLink`/`onDeepLink`）+ `package.json` 导出 `./deep-link`。
- 测试：`tests/Tarui.DeepLink.Tests` 覆盖 URL 提取/拒绝、scheme 校验、配置过滤去重、cold 播种、warm 观察者投递、按 scheme 事件、feed 复现校验路径、插件命令/权限注册、Linux `.desktop` 内容；`Tarui.SingleInstance.Tests` 增 `CoordinatorNotifiesSecondActivationSinksAsync`。
- 验收：`dotnet build` 0 警告/0 错误；自测套件全部通过（含 Architecture gate 扫描 807 文件）；`pnpm lint` 0 错误；`pnpm build` 成功。Windows 真机已验证；Linux（`.desktop` 内容已单测，`xdg-mime`/真机）与 macOS（delegate 桥）需各自平台真机验收。

**macOS warm 接线（待 macOS 真机实现/验证）**：

macOS 的 warm 激活**不经 argv**，需 AppKit `application(_:openURLs:)` delegate。做两件事：

1. `Info.plist` 声明协议（打包期配置）：
   ```xml
   <key>CFBundleURLTypes</key>
   <array>
     <dict>
       <key>CFBundleURLName</key> <string>tarui.net</string>
       <key>CFBundleURLSchemes</key> <array><string>tarui</string></array>
     </dict>
   </array>
   ```
2. 在 Cocoa AppDelegate 中把 `openURLs` 的 URL 交给现有 `DeepLinkService`（此类已暴露 `Deliver`，且实现 `ISecondActivationSink`，勿新增第二个 URL 入口）：
   ```swift
   func application(_ app: NSApplication, open urls: [URL]) {
       for url in urls {
           deepLinkBridge.deliver(url.absoluteString)   // → DeepLinkService.Deliver
       }
   }
   ```
   由于 `Deliver` 对未注册 scheme/控制字符/超长一律拒绝且不产生事件，`openURLs` 注入与 cold/转发路径共用同一校验与 `deeplink://<scheme>` 事件通道，风险面一致。cold 启动（`applicationWillFinishLaunching` 前已在 argv）由 `DeepLinkService` 构造播种覆盖，无需额外处理。

### 10.6 Updater 评估（工作项）

**目标定义**：让应用能够从可信来源（HTTPS 升级服务器）核对是否有新版本，并在经过签名校验后把整包升级到新版本，同时保留回滚能力。它不是 web-only 热更（那是开发期机制），而是对**应用整体包**（宿主 exe + CEF 原生运行时 + web dist + capabilities/schemas）的版本切换。

**候选项目**：`Tarui.Plugins.Updater`（命令/事件）+ `Tarui.Shell/UpdaterService`（清单拉取、签名校验、staging、apply 编排）+ 外部引导器/安装器策略（独立工作项）。

**复用点与界限**：

- 复用信任先例：CEF 运行时安装脚本 `eng/cef/install-runtime.ps1` 已经确立「从发布源下载并对公布哈希（SHA-1）逐份校验」的信任模式；Updater 同样以「签名清单 + 逐资产哈希」为校验基础，但把哈希校验升级为对整份清单做签名验证，才能防止仅校验不签名被中间人篡改哈希本身。
- 复用托管与配置：与 Store 一致的配置来源（`Tarui:Application:Update...`）、与 DeepLink 一致的 capability 授权与事件通道（`updater://*` 保留前缀）、与 `EventRouter` 一致的按窗口授权。
- 界限：Updater 只负责「检测 / 核验 / 编排替换」，**不得**承担插件热加载（仓库已禁运行时反射/动态加载）；升级对象是整包版本，不是进程内某个插件或某份 web 资源的 alt 切换。升级后的启动校验与回滚归属 Updater，但“以哪份包为准”必须配合单实例锁，避免多进程各换各的。

**当前事实（决定方案的前提）**：

- 发布形态：`Tarui.App` 为 framework-dependent WinExe（net10.0），CEF `win-x64` 原生运行时与 web dist 均拷贝进输出目录；无 self-contained、无安装器、无发布脚本 → 部署即”整目录拷贝“。
- 签名基础设施：仓库尚无一例代码签名证书、ECDSA（P-384/SHA-384）私钥治理（public key 注入）或升级服务器/TLS pin。**这三样在 apply 之前必须就位。**
- 信任先例：仅 CEF 运行时安装脚本做“官方 SHA-1 + 固定来源”，不足以支撑对宿主 exe 的自动替换，因为运行中的 exe/CEF DLL 在 Windows 上被锁定，无法就地原子替换。

**契约草稿**（注册 `TaruiJsonContext`）：

```csharp
public sealed record UpdateManifest(int SchemaVersion, string Version,
    string[] Files, Dictionary<string, string> Sha256, string Signature);
public sealed record UpdateCheckResult(bool UpdateAvailable, string? Version,
    string? Error);
public sealed record UpdateApplyResult(bool Succeeded, string? Error);
```

**命令与事件**：

```text
plugin:updater|check      // 拉取并校验签名清单，返回 UpdateCheckResult（只读）
plugin:updater|download   // 下载新包到受控 staging 并核验，不执行替换
plugin:updater|apply      // 在满足前置时原子替换整包并重启（默认 NOT_SUPPORTED）
事件：updater://status      // check/download/apply 状态上报（capability 显式授权）
```

**威胁模型**

| 威胁 | 缓解措施 |
| --- | --- |
| 中间人/伪造服务器下发篡改包 | HTTPS + 对整份 `UpdateManifest` 做 ECDSA（P-384/SHA-384）签名验证（公钥编译期注入，私钥离线治理）；随后逐文件 `Sha256` 复校，双重校验后才允许 staging |
| 校验与实际执行分离被绕过（检查到即执行） | `check` 只读无副作用；`download` 只写受控 staging；`apply` 单独授权且默认 `NOT_SUPPORTED`，未就绪不执行（对齐 §10 门槛“没有签名校验、回滚和安装器策略前不得交付”）。策略由 capability 控制，生产不授予 `apply` |
| CEF/DLL 运行中锁定导致替换不完整 | Windows 上就地替换不可行 → 必须走“外部引导器先关旧进程再 swap”或“安装器包”之一（见决策）；不作为 `apply` 的偷懒实现 |
| 替换过程中断导致应用不可用 | 替换前写“即将切换”清单快照；swap 双目录 + 原子改名；启动自检失败触发回滚旧包；回滚同样经签名校验 |
| 升级服务器/公钥失陷 | 公钥注入而非运行时下载；清单版本单调递增，拒绝降级；下载限速与重试有限且幂等 |
| 跨进程换版本不一致 | 复用单实例锁：只有主实例可驱动 apply，`apply` 前确认无次实例 |
| 测试/文档里伪造“已验证” | 平台矩阵只记录真机运行证据；签名 PKI、安装器、升级服务器任一未就绪时，`apply` 保持关闭而非展示通过 |

**平台矩阵**

| 平台 | 升级通道 | CEF 替换 | apply 状态 |
| --- | --- | --- | --- |
| Windows | 引导器 staged swap 或 NSIS/MSIX 安装器（未定） | 运行中 DLL 锁定，需外部进程先退出再换 | 未实现（前置未就绪） |
| macOS | `.app`/`Sparkle` 式 | Spotlight/App 更新受门禁约束 | 未实现 |
| Linux | AppImage/dpkg + 用户写权限 | 文件布局可写，相对容易 | 未实现 |

**退出标准**（apply 解锁前的前置门禁）

- `plugin:updater|check` 具有独立权限，未授权拒绝；签名/哈希核验失败返回错误而非“可更新”。
- `download` 仅写入 staging，不触碰运行目录。
- 签名 PKI（编译期注入公钥 + 离线私钥治理）、升级服务器（HTTPS + 发布清单）、安装器/引导器策略三者**全部就绪并经真机验证**，`apply` 才可被 capability 解锁。
- 回滚契约（清单快照 + 双目录 + 启动自检回滚）实现并有针对性测试。
- 事件 `updater://status` 为保留前缀，未授权窗口不接收。

**已定决策**：

- 本轮范围：实现 `check` + `download` 的完整链路（拉取、签名验证、逐文件哈希核验、受控 staging、`updater://status` 事件、capability 授权、Web 绑定与测试）——已完成(Windows)；`apply` 为“默认关闭、前置未就绪”。
- 签名算法：ECDSA（P-384 / SHA-384）——`.NET 10` BCL 未提供 `Ed25519`，改用 BCL 原生 `ECDsa` 以保证全程由系统密码学实现处理；公钥以 base64 DER `SubjectPublicKeyInfo` 编译期注入，私钥离线持有；清单递增版本禁止降级。
- 升级对象：整包（exe + CEF 原生运行时 + web dist + capabilities/schemas），staging 目录受路径白名单约束（拒绝绝对路径、盘符、反斜杠与目录穿越逃逸），升级流程不引入运行时反射/动态加载。

**验收回归（2026-08-22，Windows）**：

- 落点：`Tarui.Plugins.Updater`（`IUpdaterService` + `UpdaterPlugin` + `AddUpdaterPlugin`）、`Tarui.Shell`（`UpdaterService`、`UpdaterConfiguration`、`UpdateVerifier`）、`Tarui.Contracts`（`UpdateManifest`/`UpdateCheckResult`/`UpdateDownloadResult`/`UpdaterStatus` + `TaruiJsonContext` 注册）、`EventNames` 保留前缀 `updater://`、capabilities `plugin:updater|check`/`plugin:updater|download` 与 `updater://status` 事件、Web `updater.ts`（`check`/`download`/`onStatus`）+ barrel `@lytree/api` 与 subpath exports。
- 静态验证：`dotnet build tarui.net.sln --no-restore` 0 警告 0 错误；`Tarui.Architecture.Tests` 扫描 814 文件通过（无运行时反射/动态加载）。
- 行为测试：`Tarui.Updater.Tests` 15 例（合法签名通过、改字段签名失败、unsupported-schema、missing-hash、malformed、check 新增版本/同版本/未配置/签名失败/拉取失败、download 多文件 staging、哈希不匹配、盘符路径、穿越逃逸、未配置）+ 全量 17 套自测退出码 0；Web gate `pnpm lint` 0 错误、`pnpm build` 成功。
- 真机限制：签名 PKI、升级服务器、安装器/引导器策略未就绪 → `apply` 保持关闭；`plugin:updater|download` 仅写入本地 staging，未在任何发布源上实测端到端下载。

（升级通道中“外部引导器 swap”与“OS 安装器包”的方案对比、签名私钥治理与发布流水线，属于本工作项的后续设计，需在 `apply` 解锁前另立小节收敛。）

## 11. 项目与依赖落点

建议最终目录：

```text
src/core/
  Tarui.Contracts/
  Tarui.Ipc/
  Tarui.Capabilities/

src/desktop/
  Tarui.Hosting/
  Tarui.Shell/
  Tarui.SingleInstance/

src/plugins/
  Tarui.Plugins.FileSystem/
  Tarui.Plugins.Menu/
  Tarui.Plugins.Tray/
  Tarui.Plugins.WindowState/
  Tarui.Plugins.Notification/
  Tarui.Plugins.Autostart/
  Tarui.Plugins.GlobalShortcut/
  Tarui.Plugins.Store/
  Tarui.Plugins.Log/

web/packages/api/
  fs.ts
  menu.ts
  tray.ts
  window-state.ts
  notification.ts
  autostart.ts
  global-shortcut.ts
  single-instance.ts
  store.ts
  log.ts
```

依赖方向：

```text
Contracts <- Ipc / Capabilities <- Plugin contracts
                                  ^
Hosting / Shell / Platform implementations
                                  ^
Tarui.App composition root
```

插件项目不得直接依赖 `Tarui.Shell`。Avalonia 相关实现由 Shell 或专用 desktop 项目提供，并在组合根显式注册。

## 12. 每个功能的固定实施模板

每个新增功能按同一顺序实现：

1. 在 `Tarui.Contracts` 定义请求、结果、事件 DTO。
2. 将所有 DTO 显式加入 `TaruiJsonContext`。
3. 在独立插件项目定义服务接口、命令 Handler 和 `Add*Plugin()`。
4. 在 desktop 层实现 Windows、macOS、Linux 平台服务。
5. 在 `Tarui.App` 显式注册插件和平台服务。
6. 定义默认权限、逐命令权限和 scope schema。
7. 更新目标 Capability 文件，禁止自动授予全部权限。
8. 在 `web/packages/api` 增加独立模块和 barrel export。
9. 扩展示例应用，展示成功、取消、拒绝和不支持状态。
10. 添加插件测试、Shell/Hosting 测试、TypeScript mock 测试。
11. 更新 README、architecture 和能力矩阵。
12. 通过阶段门禁后再开始下一个功能。

## 13. 测试与阶段门禁

### 13.1 阶段入口条件

开始任一 Phase 前必须满足：

- 除 Phase 0 外，前一 Phase 的退出条件已经有测试或运行证据；Phase 0 以当前仓库基线审计为入口。
- 当前分支在修改前能够通过相关构建和测试，已知失败必须先记录。
- C# DTO、TypeScript API、权限 ID、scope 和事件名已经完成设计审查。
- 平台支持范围明确，Windows-first 项目同时写明 macOS/Linux 的计划和 `NOT_SUPPORTED` 行为。
- 涉及原生资源时已经定义 owner、释放时机和 Host 停止行为。

### 13.2 阶段退出条件

结束任一 Phase 前必须满足：

- 本 Phase 列出的命令、事件、权限、scope 和错误码全部实现。
- 正向、拒绝、非法参数、平台不支持和资源释放测试全部存在。
- 示例应用能够覆盖主要成功路径和失败状态。
- Capability 示例、README、架构文档和 API exports 已同步。
- 支持矩阵更新为实际验证结果，不以“理论可编译”代替运行证据。
- 所有阶段门禁命令通过，或有明确记录且未将 Phase 标记为完成。

每阶段至少执行：

```powershell
dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln --no-restore
dotnet run --project tests/Tarui.Ipc.Tests --no-build
dotnet run --project tests/Tarui.WebView.Tests --no-build
dotnet run --project tests/Tarui.Shell.Tests --no-build
dotnet run --project tests/Tarui.Plugins.Tests --no-build
dotnet run --project tests/Tarui.Hosting.Tests --no-build
dotnet run --project tests/Tarui.Architecture.Tests --no-build

cd web
pnpm install --frozen-lockfile
pnpm lint
pnpm build
```

新增测试项目：

- `Tarui.Capabilities.Tests`：manifest、scope、事件权限、profile 和 schema。
- `Tarui.FileSystem.Tests`：路径边界、链接、大小限制和原子写入。
- `Tarui.SingleInstance.Tests`：真实双进程竞争和激活转发。
- `Tarui.DesktopIntegration.Tests`：菜单、托盘、通知和快捷键资源生命周期。
- `@lytree/api` mock 测试：请求序列化、事件解绑、错误码和 API 类型。

阶段完成标准：

- 零编译警告，所有警告继续视为错误。
- Architecture Tests 未放宽无反射、无扫描、无动态加载约束。
- 新命令都有权限和反向拒绝测试。
- 新事件都有路由范围和未授权窗口测试。
- 所有原生资源都能在窗口或 Host 关闭时释放。
- Windows、macOS、Linux 支持状态记录在能力矩阵中。
- 文档、Capability 示例、C# 契约和 TypeScript API 保持同步。

## 14. 推荐执行顺序

严格按以下顺序推进：

1. Capability v2、事件权限、显式窗口 profile。
2. 路径安全、优雅退出、WebView 和原生资源释放。
3. 受限文件系统。
4. 原生菜单。
5. 单实例和窗口状态。
6. 托盘与 Explicit ShutdownMode。
7. 通知和自动启动。
8. 全局快捷键。
9. 文件拖放和标题栏拖拽。
10. Store、Logging、Deep Link、Updater 等产品化插件。

不建议先做拖放、更新器或任意文件访问。它们分别依赖 WebView 原生事件、发布签名体系和资源级权限，跳过前置阶段会留下难以兼容的协议和安全债务。

## 15. 实施状态跟踪

| 里程碑 | 总体 | Windows | macOS | Linux | 工作项/证据 | 最后复核 |
| --- | --- | --- | --- | --- | --- | --- |
| 对齐分析与步骤文档 | 已完成 | 不适用 | 不适用 | 不适用 | 本文及 README 入口 | 2026-08-21 |
| Tauri-to-Tarui 模块映射 | 已完成 | 不适用 | 不适用 | 不适用 | 第 2.1、2.2 节 | 2026-08-21 |
| Phase 0：安全与生命周期 | 已完成(Windows) | 验证中 | 未验证 | 未验证 | 4.1 Capability v2 已完成：manifest DTO、scope、events、JSON Schema、启动校验、scope 授权器。4.2 显式窗口权限配置已完成：`WindowCapabilityResolver`、`CapabilityNotFoundException`、`*-other-window` 守卫、创建提权防护。4.3 系统事件保护已完成：Web emit 限定 `user://`（`EventNames`）、保留原生前缀、`EventRouter` 按 capability `events` 做接收授权。4.4 路径安全已完成：`IFileAccessPolicy`（`Tarui.Ipc/FileAccessPolicy`）统一授权 gate，`PathAccessDeniedException`→`PATH_DENIED`；拒绝 rooted/UNC-设备/控制字符/空`.``..`/ADS 段，单词段 `ResolveLinkTarget` 符号链接/重解析越界防护，临时文件+原子替换写入，单次与累计大小上限；`Tarui.Capabilities.Tests` 新增 5 组路径测试（拒绝、合法、链接逃逸、大小预算、原子写），Windows 无开发者模式时链接测试按环境跳过。4.5 生命周期已完成：`IAppShutdown`/`IAppShutdownCoordinator`（`Tarui.Ipc/AppShutdown`）、`HostAppShutdown`→`IHostApplicationLifetime.StopApplication`、`AppShutdownMode` 三档由 `Tarui:Application:ShutdownMode` 配置，`ShellWindowFactory` 关闭时释放 WebViewHost 并通知 coordinator，`ProcessService` 改走 host 协调退出。验收已通过：`dotnet build` 0 警告/0 错误；Ipc/WebView/Shell/Plugins/Hosting/Capabilities/Architecture 7 套自测试全绿；web lint 0 错误、web build 成功。macOS/Linux 平台未运行验证 | 2026-08-21 |
| Phase 1：受限文件系统 | 进行中 | 已验证 | 未验证 | 未验证 | 1.1 契约：`Tarui.Contracts/FileSystemContracts.cs` 定义 `FsPathOptions` 等 10 组 DTO，并注册 `TaruiJsonContext` 序列化元数据（含 `bool`、`Unit`、`FsDirEntry[]`）。1.2 插件：`Tarui.Plugins.FileSystem/FileSystemPlugin.cs` + `FileSystemService.cs` 九条命令（`read-text-file`、`write-text-file`、`read-dir`、`stat`、`exists`、`mkdir`、`copy-file`、`rename`、`remove`），命令路由 + 9 个独立权限，`FsScopeAuthorizer` 实现 `**` 跨多级 + `**/suffix` + `/*` glob 与 deny 优先，写类命令拒绝只读 `resources`，路径授权复用 `IFileAccessPolicy`（含 rooted/UNC/控制字符/空段/`..`/符号链接越界 + 8 MiB 文本单次上限 + 原子临时替换写入）。1.3 集成：`Tarui.App/Program.cs` 增加 `AddFileSystemPlugin()`，`Tarui.App.csproj` + `Tarui.Plugins.Tests.csproj` 加入插件引用，`tarui.net.sln` 加入 `Tarui.Plugins.FileSystem` 与 `Tarui.FileSystem.Tests` 项目。1.4 Capabilities：`capabilities/main.json` 授予 main 窗口结构化 scoped FS 权限（读 `documents/**` + `**/*.json` + `appLog/temp/resources`，写排除只读 `resources`，`remove` 附加 deny `appConfig/settings/protected.json`）；`editor.json` 仅授予只读域（appData/documents/** + resources）。1.5 测试：`tests/Tarui.FileSystem.Tests/` 新增 8 组自测试（原子写往返 + 大小预算 SizeLimit 拒绝 + resources 写拒绝 + mkdir/remove 递归树 + read-dir 尺寸/类型 + stat/exists 磁盘一致性 + copy/rename 字节迁移 + scope allow/deny/glob 正反 + 9 条命令注册计数）。1.6 Web API：`web/packages/api/fs.ts` 新增 base 枚举 + 9 条类型化调用，`index.ts` barrel export fs 模块。1.7 验收 Windows：`dotnet build tarui.net.sln` 0 警告/0 错误；`Tarui.FileSystem.Tests`、`Tarui.Capabilities.Tests`、`Tarui.Plugins.Tests`、`Tarui.Architecture.Tests`、`Tarui.Ipc.Tests`、`Tarui.WebView.Tests`、`Tarui.Shell.Tests`、`Tarui.Hosting.Tests` 八套自测试全部退出码 0；`pnpm lint` 0 错误、`pnpm build` 成功。macOS/Linux 平台未运行验证。 | 2026-08-21 |
| Phase 2：菜单与托盘 | 已完成(Windows) | 验证中 | 未验证 | 未验证 | 2.1 契约：`Tarui.Contracts/MenuTrayContracts.cs` 定义 `MenuItemDefinition`（normal/divider/check/submenu + 稳定 `id` + enabled/checked/accelerator/嵌套 items）、`SetWindowMenuOptions`、`MenuUpdateItemOptions`、`TrayCreateOptions` 及 set-menu/set-icon/set-tooltip/set-visible/remove 等 11 组 DTO 与 3 组事件 DTO，并注册 `TaruiJsonContext` 序列化元数据。2.2 插件：`Tarui.Plugins.Menu/MenuPlugin.cs` 三条命令（`set-window-menu`、`update-item`、`remove-window-menu`）、`Tarui.Plugins.Tray/TrayPlugin.cs` 六条命令（`create`、`set-menu`、`set-icon`、`set-tooltip`、`set-visible`、`remove`），全部走 owner 语义（`CommandContext` 授权 + owner window 归属校验）。2.3 Shell 集成：`Tarui.Shell` 新增 `AvaloniaMenuService`（`IMenuService`，基于 Avalonia `NativeMenu` + `NativeMenu.SetMenu` 附加属性、`NativeMenuItemToggle`→`MenuItemToggleType`）与 `AvaloniaTrayService`（`ITrayService` + `IDisposable`，`TrayIcon` + `TrayIcon.SetIcons(Application, TrayIcons)` 集合注册，图标经 `TrayIconPath.Resolve` 解析 `resources:`/`temp:` 等 base）；`NativeMenuBuilder` 校验整树 id 唯一并构建子菜单；服务经 `TaruiShellServiceCollectionExtensions` 注册，owner 窗口销毁时自动释放菜单/托盘（生命周期关闭钩子）；`Tarui.Shell.csproj` 加入 Menu/Tray 插件引用，`Tarui.App/Program.cs` 增加 `AddMenuPlugin()` + `AddTrayPlugin()`。2.4 Capabilities：`capabilities/main.json` + `editor.json` 授予 3 条 menu + 6 条 tray 权限，事件表注册 `menu://item-clicked`、`tray://clicked`、`tray://menu-item-clicked`。2.5 测试：`tests/Tarui.MenuTray.Tests/` 新增 9 组自测试（Menu/Tray 插件全命令注册 + dispatch 转发 owner 并做权限 gate + 整树 id 唯一 + 嵌套 id 拒绝 + 图标路径 base 解析正反 + 点击 DTO JSON 往返）。2.6 Web API：`web/packages/api/menu.ts`（3 条调用 + `menuItemKinds`）+ `tray.ts`（6 条调用），`package.json` exports 增加 `./menu`、`./tray`（并补 `./fs`），`index.ts` barrel export。2.7 验收 Windows：`dotnet build tarui.net.sln` 0 警告/0 错误；Ipc/WebView/Shell/Plugins/Hosting/Capabilities/FileSystem/MenuTray/Architecture 九套自测试全部退出码 0；`pnpm lint` 0 错误、`pnpm build` 成功。macOS/Linux 平台未运行验证。 | 2026-08-21 |
| Phase 3：单实例与窗口状态 | 已完成(Windows) | 验证中 | 未验证 | 未验证 | 3.1 契约：`Tarui.Contracts/SingleInstanceStateContracts.cs` 定义 `SecondInstanceArgs`（arguments + workingDirectory + timestamp）、`WindowStateOptions`/`WindowStateSaveOptions`/`WindowStateRestoreOptions`、`WindowStateSnapshot`（label + x/y/width/height + maximized/fullscreen + scaleFactor）、`WindowStateRestoreResult`，并注册 `TaruiJsonContext` 序列化元数据。3.2 单实例：`Tarui.SingleInstance` 项目 `SingleInstanceGuard`（`Acquire` 抢占：Windows 命名 Mutex + 命名管道转发、macOS/Linux 走 Unix 域 socket 转发路径；主实例返回 `SingleInstanceHandle(InstanceRole.Primary)`，次实例序列化 `SecondInstanceArgs` 转发后返回 `Secondary`）+ `SingleInstanceCoordinator`（`Start()`/`Dispose()` 起停监听，`Receive(args)` 在主窗口未注册时入队、`Flush()` 随 main 窗口就绪投递 `app://second-instance` 事件；投递经 `EventRouter` 按 capability `events` 做接收授权）+ `AddSingleInstance` 扩展（DI 注册 identity 读取 `Tarui:Application:SingleInstance` 配置）。3.3 接线：`Tarui.App/Program.cs` 先 `RunSubProcess` 再 `SingleInstanceGuard.Acquire`，主进程注册生命周期、MainWindow 创建后 `coordinator.Flush()`，`Tarui.App.csproj` + `Tarui.SingleInstance` + `tarui.net.sln` 集成。3.4 窗口状态插件：`Tarui.Plugins.WindowState/WindowStatePlugin.cs` 三条命令（`plugin:window-state|save`、`restore`、`clear`）+ `WindowStatePermissionGuard` owner 与 `-other-window` 变体授权，纯模型适配器 `WindowStateFit.ClampToMonitors`（把离线快照拉回到主工作区、钳制尺寸不超当前显示器），Shell 实现 `AvaloniaWindowStateService`（`IWindowStateService`，经 `IWindowService` + `JsonWindowStateStore` 读写几何与最大化/全屏状态）；契约 `IWindowStateStore` 下沉到插件避免 Shell↔plugin 环依赖。3.5 接线：`AddTaruiShell` 注册窗口状态服务，`Tarui.App/Program.cs` 增加 `AddWindowStatePlugin()`，`Tarui.Shell` 引用 WindowState 插件，`tarui.net.sln` + capabilities 集成。3.6 Web API：`web/packages/api/single-instance.ts`（`onSecondInstance` + `SecondInstanceArgs` 类型）+ `window-state.ts`（`save`/`restore`/`clear` + `WindowStateOptions`/`WindowStateRestoreResult`），`package.json` exports 增加 `./single-instance`、`./window-state`，`index.ts` barrel export。3.7 测试：`tests/Tarui.SingleInstance.Tests/`（主进程抢占 + 次实例 `--si-probe` 双进程转发参数/工作目录 + 未注册窗口入队 + `Flush()` 投递 + 未授权窗口不投递 + Start/Dispose 往返）+ `tests/Tarui.WindowState.Tests/`（`WindowStateFit` 几何拟合：离线/越界/超大快照拉回主工作区 + save/restore/clear 命令注册、权限 gate、`-other-window` 守卫、restore 结果 JSON 往返）。3.8 验收 Windows：`dotnet build tarui.net.sln` 0 警告/0 错误；SingleInstance/WindowState 新增 2 套 + Ipc/WebView/Shell/Plugins/Hosting/Capabilities/FileSystem/MenuTray/Architecture 共 11 套自测试全部退出码 0；`pnpm lint` 0 错误、`pnpm build` 成功。macOS/Linux 平台未运行验证。 | 2026-08-21 |
| Phase 4：通知、自动启动、全局快捷键 | 已完成(Windows) | 验证中 | 未验证 | 未验证 | 4.1 契约：`Tarui.Contracts/NotificationContracts.cs`（`NotificationOptions`/`NotificationPermissionStateResult`/`NotificationCancelOptions`/`NotificationEvent`）+ `AutostartContracts.cs`（`AutostartEnableOptions`/`AutostartState`）+ `GlobalShortcutContracts.cs`（`GlobalShortcutOptions`/`GlobalShortcutState`/`GlobalShortcutTriggered`），并注册 `TaruiJsonContext` 序列化元数据。4.2 通知插件：`Tarui.Plugins.Notification/NotificationPlugin.cs` 四条命令（`permission-state`/`request-permission`/`show`/`cancel`）+ `NotificationValidator` 纯逻辑校验（id/title/body 非空且超长拒绝）。4.3 自动启动插件：`Tarui.Plugins.Autostart/AutostartPlugin.cs` 三条命令（`is-enabled`/`enable`/`disable`）+ `AutostartConfig` 参数校验（数量/单参长度/控制字符）+ `BuildCommandLine` 始终引用当前进程路径并引用转义。4.4 全局快捷键插件：`Tarui.Plugins.GlobalShortcut/GlobalShortcutPlugin.cs` 四条命令（`register`/`unregister`/`unregister-all`/`is-registered`）+ `AcceleratorSpec` 归一化（`+`/`-` 分隔、修饰符别名 `Ctrl/Control`、`Cmd/Command/Super/Meta/Win`、`Option/Alt`，key 大写，修饰符不得在 key 之后）+ `AcceleratorScopeAuthorizer` 作用域授权（deny 优先、`allow` glob 匹配、作用域模式归一化识别别名）。4.5 Shell 平台服务：`Tarui.Shell` 新增 `WindowsNotificationService`（Shell_NotifyIcon 气泡 + 按 id 去重，非 Windows 诚实降级）、`WindowsAutostartService`（注册表 `Run` 键 + 命令行拼接）、`WindowsGlobalShortcutService`（RegisterHotKey + 隐藏消息窗口线程 + `WM_HOTKEY` 投递 `global-shortcut://triggered`）；事件统一经 `EventRouter` 按 capability `events` 授权投递 `notification://activated/dismissed`、`global-shortcut://triggered`；服务经 `TaruiShellServiceCollectionExtensions` 注册，并向 App 组合根暴露。4.6 接线：`Tarui.App/Program.cs` 增加 `AddNotificationPlugin()`/`AddAutostartPlugin()`/`AddGlobalShortcutPlugin()`，`Tarui.App.csproj`/`Tarui.Shell.csproj`/`tarui.net.sln` 集成，`capabilities/main.json`+`editor.json` 授予权限与事件、main 授予 global-shortcut 作用域 allow/deny（`Ctrl+Shift+P`/`Ctrl+Alt+*`/`Ctrl+Alt+F4`）。4.7 Web API：`web/packages/api/notification.ts`（4 条调用 + `onNotificationActivated`/`onNotificationDismissed`，经 `listen` 收 `notification://*`）+ `autostart.ts`（3 条）+ `global-shortcut.ts`（4 条 + `onGlobalShortcut`），`package.json` exports 增加 `./notification`/`./autostart`/`./global-shortcut`，`index.ts` barrel export。4.8 测试：`tests/Tarui.Notification.Tests`/`Tarui.Autostart.Tests`/`Tarui.GlobalShortcut.Tests` 三套新增（命令注册计数 + dispatch 授权 gate + 校验拒绝空/超长 + `AcceleratorSpec` 归一化与作用域 glob 匹配 + `AutostartConfig` 引用/参数校验 + DTO JSON 往返）。4.9 验收 Windows：`dotnet build tarui.net.sln` 0 警告/0 错误；新增 3 套 + Ipc/WebView/Shell/Plugins/Hosting/Capabilities/FileSystem/MenuTray/SingleInstance/WindowState/Architecture 共 14 套自测试全部退出码 0；`pnpm lint` 0 错误、`pnpm build` 成功（`@lytree/api` 新 TS 经 web `tsc -b` 传递类型检查）。macOS/Linux 平台未运行验证。 | 2026-08-21 |
| Phase 5：WebView 深度桌面集成 | 进行中 | 验证中 | 未验证 | 未验证 | 5.1 契约：`Tarui.Contracts/WebViewContracts.cs` 定义 `WebViewFileDropEvent`（paths/text/x/y）、`WebViewDownloadRequestEvent`（url/suggestedFilename）、`WebViewNavigationRequestEvent`（url/isMainFrame），注册 `TaruiJsonContext` 序列化元数据。5.2 纯策略引擎：`Tarui.WebView.Abstractions/WebViewRequestPolicy.cs`（`WebViewPolicyOptions` + `WebViewRequestDecision.Allow/External/Deny`，导航 glob allow/external 默认 deny，下载 host allow 默认 deny，拒绝非 http/https scheme 与 URL 控制字符，相对 URL 抛 `WebViewRequestDeniedException(MalformedUrl)`）+ `DraggableRegion.cs`（`DraggableRegionSelector.HitTest` 实现 NoDrag 覆盖 Drag、`Differs` 集合差异比较）。5.3 抽象扩展：`Tarui.WebView.Abstractions/IWebView.cs` 为 `ITaruiWebView` 增加类型化原生事件 `FileDropEntered/FileDropLeft/FileDropped/DownloadRequested/NavigationRequested/DragRegionsUpdated` 及 `Navigate/SetDragRegions`。5.4 Shell 路由：`Tarui.Shell/WebViewHost.cs` 承接原生事件→先经 `WebViewRequestPolicy` 决策导航/下载，再按 capability `events` 授权将带窗口上下文的事件经 `EventRouter` 投递 `window://file-drop-entered/left/dropped`、`webview://download-requested/navigation-requested`；未授权窗口在 OS 层拒绝拖放（`Accepted=false`），绝不携带文件路径。5.5 CefGlue 适配：`Tarui.WebView.CefGlueNext` 新增 `CefGlueNextNativeHandlers.cs`（`CefNavigationRequestHandler.OnBeforeBrowse` 决策并对外部打开、`CefDownloadHandler.OnBeforeDownload` 决策、`CefDragHandler.OnDraggableRegionsChanged` 转换），`CefGlueNextWebView` 接线 RequestHandler/DownloadHandler/DragHandler。5.6 接线/API：`TaruiShellServiceCollectionExtensions` 用可选 `IConfiguration` 构建 `WebViewRequestPolicy`（`Tarui:Web:Policy:*` 默认允许应用源与 localhost，外部 `https:*`），`appsettings.json` 配 `NavExternal: https:*`，`capabilities/main.json`+`editor.json` events 注册 5 个保留事件；`web/packages/api/window.ts` 增加 `FILE_DROP_*_EVENT`/`DOWNLOAD_REQUESTED_EVENT`/`NAVIGATION_REQUESTED_EVENT` + `FileDropEvent`/`DownloadRequestEvent`/`NavigationRequestEvent` 类型 + `onFileDropEntered/onFileDropLeft/onFileDropped/onDownloadRequested/onNavigationRequested`，`index.ts` barrel export。5.7 测试：`Tarui.WebViewEvents.Tests`（策略决策 + 恶意 scheme/控制字符 + 相对 URL + glob 语义 + 拖拽区域命中/NoDrag 覆盖/退化/差异）+ `Tarui.Shell.Tests` 新增 WebViewHost 路由授权（未授权文件拖放 OS 层拒绝、授权分发路径/文本/坐标、下载按 policy+capability 正反、导航按 policy+capability 正反）。5.8 验收 Windows（代码与门禁）：`dotnet build tarui.net.sln` 0 警告/0 错误；16 套自测试全部退出码 0（含 Architecture 扫描 787 files）；`pnpm lint` 0 错误、`pnpm build` 成功。真机 windowed CEF 验收待执行：文件进入/离开/放下、多窗口拖放命中、拖拽区域动态更新、下载/导航外部打开、WebView 反复创建销毁无残留。macOS/Linux 平台未运行验证。 | 2026-08-21 |
| Phase 6：产品化插件（Store + Logging）| 进行中 | 已验证 | 未验证 | 未验证 | 6.1 契约：`Tarui.Contracts/StoreContracts.cs`（`StoreFileOptions`/`StoreKeyOptions`/`StoreSetOptions`/`StoreGetResult`/`StoreHasResult`/`StoreKeysResult`）+ `LogContracts.cs`（`LogRecordOptions`/`LogEntry`），均注册 `TaruiJsonContext` 序列化元数据；`Tarui.Ipc/EventNames.cs` ReservedPrefixes 增加 `log://`。6.2 Store 插件：`Tarui.Plugins.Store/StorePlugin.cs` 暴露 `IStoreService`（get/set/has/delete/clear/keys 六条命令 `plugin:store|*`）+ `StoreScopeAuthorizer`（按 capability allow/deny scope 校验 base+path，deny 优先）。6.3 Store 实现：`Tarui.Plugins.Store/JsonStoreService.cs` 复用 `IFileAccessPolicy`（`ReadAllBytesAsync`/`WriteAllBytesAtomicAsync` 原子替换、`resources` 只读、PATH_DENIED 错误码）做 JSON 配置持久化，内存 `Dictionary` 缓存 + 锁快照写盘，`resources` base 拒绝写。6.4 Logging 插件+Shell：`Tarui.Plugins.Log/LogPlugin.cs`（`plugin:log|record`）+ `LogService`（把渲染进程日志桥接进 `Microsoft.Extensions.Logging`，未知 level 降级 `Information`、默认 category `renderer`、`BeginScope` 携带 `TaruiTimestamp`）+ `RemoteLoggerProvider`/`RemoteLogger`（`ILoggerProvider`，把桌面日志投递到 `IRemoteLogSink`，`LogLevel.None` 过滤）；`Tarui.Shell/RemoteLogSink.cs` 经 `EventRouter` 按 capability `events` 授权广播 `log://entry`。6.5 接线：`Tarui.App/Program.cs` `AddStorePlugin()`+`AddLogPlugin()`，`Tarui.App.csproj`/`Tarui.Shell.csproj` 引用 Store/Log 插件，`TaruiShellServiceCollectionExtensions` 注册 `IRemoteLogSink`+`ILoggerProvider`，`capabilities/main.json`+`editor.json` 授予 store 权限（含 appData/appConfig scoped allow 与 `resources` deny）与 `log://entry` 事件接收。6.6 Web API：`web/packages/api/store.ts`（6 条调用 + `storeBaseIdentifiers`）+ `log.ts`（`record`+`trace/debug/info/warn/error/critical` + `onLogEntry` + `logLevels` + `LOG_ENTRY_EVENT`），`package.json` exports 增加 `./store`、`./log`，`index.ts` barrel export（含 `log`/`store` 对象与类型）。6.7 测试：`tests/Tarui.Store.Tests`（11 组：Set/Get 往返 + 文件落盘、null 擦除 key、Has、Delete、Clear、Keys、缺失文件读空、新服务重载持久化、resources 写拒绝、scope allow/deny/通配、六命令注册计数）+ `tests/Tarui.Log.Tests`（7 组：level/category 转发、未知 level 降级、默认 category、命令注册、RemoteLogger 类别/格式/异常追加、None 过滤）。6.8 验收 Windows：`dotnet build tarui.net.sln` 0 警告/0 错误；Store/Log 新增 2 套 + Ipc/WebView/Shell/Plugins/Hosting/Capabilities/FileSystem/MenuTray/SingleInstance/WindowState/Notification/Autostart/GlobalShortcut/WebViewEvents/Architecture 共 17 套自测试全部退出码 0（Architecture 扫描 797 files，无反射/扫描/动态加载）；`pnpm lint` 0 错误、`pnpm build` 成功（store/log 经 tsc -b 传递类型检查）。威胁模型与平台矩阵见 Phase 6 备注；macOS/Linux 平台未运行验证。后续按优先级评估 Deep Link、Updater、HTTP、SQL。 | 2026-08-21 |

状态更新规则：

1. “未开始”改为“进行中”时，必须链接对应实现分支、Issue 或提交范围。
2. “进行中”改为“已完成”时，必须填写构建、测试和平台运行证据。
3. 某个平台未验证时，里程碑不能笼统标记为跨平台完成。
4. 本表是计划状态的权威入口，README 只链接本文，不维护第二份状态。

## 16. 官方对齐参考

本文以 2026-08-21 可访问的 Tauri v2 官方文档为语义参考：

- [Capabilities](https://v2.tauri.app/security/capabilities/)
- [Permissions](https://v2.tauri.app/security/permissions/)
- [Command Scopes](https://v2.tauri.app/security/scope/)
- [File System](https://v2.tauri.app/plugin/file-system/)
- [System Tray](https://v2.tauri.app/learn/system-tray/)
- [Window Menu](https://v2.tauri.app/learn/window-menu/)
- [Single Instance](https://v2.tauri.app/plugin/single-instance/)
- [Notifications](https://v2.tauri.app/plugin/notification/)
- [Autostart](https://v2.tauri.app/plugin/autostart/)
- [Global Shortcut](https://v2.tauri.app/plugin/global-shortcut/)

实现时仍应以仓库锁定的 Avalonia、CEF 和 .NET 版本为准；若官方 Tauri API 后续变化，应先更新本文的基线日期和差异说明，再调整 Tarui 契约。
