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
| Deep Link / Updater | 未实现 | 尚无协议激活和更新流程 |
| File Drop / Drag Region | 未实现 | 当前 Avalonia 12 windowed CEF 适配未暴露对应事件 |

现有 5 个插件共注册 35 个权限匹配命令。新增能力必须继续沿用以下链路：

```text
Tarui.Contracts DTO
  -> ITaruiPlugin 显式注册
  -> Shell/Hosting/平台服务实现
  -> Capability 授权
  -> @tarui/api 类型化模块
  -> 控制台式自测试
```

### 2.1 Tauri v2 到 Tarui 的模块映射

| Tauri v2 表面 | Tarui 当前表面 | 状态 | 目标阶段 |
| --- | --- | --- | --- |
| `@tauri-apps/api/core` invoke / Channel | `@tarui/api/ipc` | 部分对齐：invoke 可用，Channel 未接线 | Phase 0 / 后续 Channel 专项 |
| `@tauri-apps/api/app` | `@tarui/api/app` | 部分对齐：握手可用，产品信息硬编码 | Phase 0 |
| `@tauri-apps/api/window` | `@tarui/api/window` | 部分对齐：主要窗口控制已实现 | Phase 0 / Phase 5 |
| `@tauri-apps/api/webviewWindow` | 合并在 Window 与 WebViewHost | 已规划：暂不强制拆成独立对象 | Phase 5 |
| `@tauri-apps/api/event` | `@tarui/api/event` | 部分对齐：缺少系统事件保护和接收权限 | Phase 0 |
| `@tauri-apps/api/path` | `@tarui/api/path` | 部分对齐：目录解析可用，路径边界需加固 | Phase 0 |
| Dialog plugin | `@tarui/api/dialog` | 部分对齐：文件、目录和保存选择可用 | Phase 1 会话 grant |
| Opener plugin | `@tarui/api/shell` | 部分对齐：默认程序打开可用，缺少 scope | Phase 0 |
| Clipboard Manager plugin | `@tarui/api/clipboard` | 部分对齐：仅文本 | 后续增强 |
| OS plugin | `@tarui/api/os` | 部分对齐：基础系统信息可用 | Phase 0 功能矩阵 |
| Process plugin | `@tarui/api/process` | 部分对齐：退出和重启需纳入 Host 生命周期 | Phase 0 |
| File System plugin | 无 | 已规划 | Phase 1 |
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
| File System | `fs.ts` | `Tarui.Plugins.FileSystem` | `plugin:fs|*`、后续 `fs://*` | allow/deny path scope、链接和大小测试 |
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
import { getCurrentWindow } from '@tarui/api/window'
import { readTextFile } from '@tarui/api/fs'
import { TrayIcon } from '@tarui/api/tray'
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

1. 优先提供与 Tauri 熟悉用法接近的独立 TypeScript 子路径，例如 `@tarui/api/fs` 和 `@tarui/api/tray`。
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
CefGlueRuntimeBootstrap.RunSubProcess(args)
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

主窗口未注册时进入有界队列，注册后发送 `app://second-instance`。Windows 使用命名 Mutex 加 Named Pipe；macOS/Linux 使用进程锁加本地 socket，并保证通信端点只对当前用户开放。

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

web/packages/api/
  fs.ts
  menu.ts
  tray.ts
  window-state.ts
  notification.ts
  autostart.ts
  global-shortcut.ts
  single-instance.ts
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
- `@tarui/api` mock 测试：请求序列化、事件解绑、错误码和 API 类型。

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
| Phase 0：安全与生命周期 | 未开始 | 未验证 | 未验证 | 未验证 | 尚无对应代码和测试变更 | 2026-08-21 |
| Phase 1：受限文件系统 | 未开始 | 未验证 | 未验证 | 未验证 | 尚无插件项目 | 2026-08-21 |
| Phase 2：菜单与托盘 | 未开始 | 未验证 | 未验证 | 未验证 | 尚无插件项目 | 2026-08-21 |
| Phase 3：单实例与窗口状态 | 未开始 | 未验证 | 未验证 | 未验证 | 尚无 desktop/plugin 项目 | 2026-08-21 |
| Phase 4：通知、自动启动、全局快捷键 | 未开始 | 未验证 | 未验证 | 未验证 | 尚无插件项目 | 2026-08-21 |
| Phase 5：WebView 深度桌面集成 | 未开始 | 未验证 | 未验证 | 未验证 | WebView 抽象尚未扩展 | 2026-08-21 |
| Phase 6：产品化插件 | 未开始 | 未验证 | 未验证 | 未验证 | 等待桌面核心稳定 | 2026-08-21 |

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
