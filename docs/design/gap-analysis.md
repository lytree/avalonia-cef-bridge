# Tarui 与 Wails v3 / Tauri v2 功能差距分析

> 状态：分析基线（2026-10-16 实测复核）
> 基线日期：2026-10-16（本次以代码实测复核，见 §1.1）
> 对比对象：Tauri v2 官方插件与核心 API（**tauri 2.12.3** / updater 2.13.3，2026-10-10）、Wails v3（alpha）
> 姊妹文档：[tauri-desktop-alignment-plan.md](./alignment-plan.md)（实施步骤与状态跟踪的权威入口，本文不重复其阶段门禁）、[`./architecture.md`](architecture.md)（架构总览）
> 本文目的：静态盘点当前仓库实际能力，对照两个对标框架输出缺口清单与可优化项，供排期决策。

## 1. 结论摘要

- Tarui 已完成桌面壳层核心：无反射 IPC、多窗口、CEF 150 WebView（自定义协议/导航与下载策略/文件拖放）、Capability v2 权限模型、Channel 端到端流式 IPC（含 fs 大文件流式读写与 HTTP 流式响应、Shell 子进程 stdio 流）、**27 套自测试**、**32 个前端 API 模块**。
- Tauri v2 桌面相关 25 个官方能力中：已对齐约 **23 个**（多为 Windows 验证）、部分对齐 1 个（deep-link 待真机验收）；**SQL 已于 2026-10-16 落地**，剩余缺失仅 **stronghold**（加密存储）。
- Wails v3 桌面能力（多窗口/托盘/菜单/事件/对话框/拖放/frameless）基本具备等价实现；父子窗口 + modal、可取消 close 钩子与托盘附着定位（Positioner）已补齐，剩余显著差距在开发体验（bindings 自动生成）。
- **最高优先缺口（P0）**：桌面框架核心能力已全部落地。当前唯一的**架构级缺口**是**单窗口多 WebView**（Tarui 为窗口↔webview 一对一模型，Tauri v2 的 Window 与 Webview 是独立实体）；其余为平台真机验收与 P2 按需增强。
- 平台现实：所有原生能力当前仅 Windows 完成验证；autostart 已有 macOS/Linux 实现，notification/global-shortcut 在非 Windows 为诚实降级并通过 `core:platform|capabilities` 如实暴露。CLI 新增 `app-bundle` target 产出 `.app` / `.app.tar.gz`，`InfoPlistBuilder` 注入 `CFBundleURLTypes`，为 macOS 真机验收打通链路；签名/公证留待 macOS runner 接入（[ADR-0002](../adr/0002-macos-real-build-pipeline.md)）。

### 1.1 本次复核方法与修正记录（2026-10-16）

本文此前存在三处与代码不符的描述，本次按实测修正，后续修改文档应先复核再落笔：

| 项 | 原文描述 | 实测事实 |
| --- | --- | --- |
| Tauri 版本基线 | v2.11.0（`tauri.app/release/tauri/v2.11.0/`） | **tauri 2.12.3**、cli 2.12.2、`@tauri-apps/api` 2.12.3（2026-10-10）；`updater` 插件已 2.13.3 |
| 能力清单位置 | 多处写 `capabilities/*.json`（易读作仓库根） | 仓库根 `capabilities/` 为**未追踪的空目录**；权威位置是**各应用自己的** `capabilities/`，演示应用即 `examples/demo/capabilities/{main,editor}.json`，构建时复制到输出目录 |
| 测试数量 | "26 套" | 实测 **27 套**全部通过（2026-10-16 新增 `Tarui.Sql.Tests`） |

复核命令：`dotnet build tarui.net.slnx`（0 警告 0 错误）、逐个 `dotnet run --project tests/Tarui.*.Tests --no-build`（27/27 通过）、`web/packages/api` 下 `vitest run`（49/49 通过）。

## 2. Tarui 当前能力基线（2026-10-16 实测快照）

| 能力域 | 状态 | 关键落点 |
| --- | --- | --- |
| IPC（无反射、源生成元数据） | 已实现 | `Tarui.Ipc/CommandRouter.cs`、`Tarui.Contracts` DTO + `TaruiJsonContext` |
| Capability v2（allow/deny scope、glob、事件接收授权） | 已实现 | `Tarui.Ipc/CapabilitySet.cs`、`examples/demo/capabilities/*.json`（各应用自有目录） |
| 多窗口管理（创建/显隐/焦点/置顶/全屏/去装饰/显示器） | 已实现 | `Tarui.Shell/AvaloniaWindowService.cs`、`Tarui.Plugins.Window` |
| WebView（CEF 150.x、`tarui://` 自定义协议、HTTP 模式、CSP、SPA fallback） | 已实现 | `Tarui.WebView.CefGlueNext/CefGlueNextWebViewFactory.cs`、`CefGlueNextWebAppOptions.cs` |
| 导航/下载策略（allow/external/deny glob、host allow 默认 deny） | 已实现 | `Tarui.WebView.Abstractions/WebViewRequestPolicy.cs` |
| 文件拖放 + 拖拽区域（NoDrag 覆盖、差异比较） | 已实现 | `DraggableRegion.cs`、`CefGlueNextNativeHandlers.cs`、`webview://*`、`window://file-drop-*` 事件 |
| 事件系统（emit 定向/广播、`user://` 前缀保护、原生前缀保留） | 已实现 | `Tarui.Shell/EventRouter.cs`、`Tarui.Plugins.Event` |
| Dialog（open/save/message/confirm） | 已实现 | `Tarui.Plugins.Dialog`、`Tarui.Shell/AvaloniaDialogService.cs` |
| FileSystem（16 条命令、scoped glob、原子写、流式读/分片写、目录 watch、`fs://watch-change`） | 已实现 | `Tarui.Plugins.FileSystem/FileSystemService.cs`、`FsScopeAuthorizer` |
| Store（JSON KV、scope、原子写） | 已实现 | `Tarui.Plugins.Store/JsonStoreService.cs` |
| SQL（SQLite 驱动、参数化绑定、类型保真、句柄生命周期、scope 授权） | 已实现 | `Tarui.Plugins.Sql/SqlService.cs`、`SqliteDriver`、`tests/Tarui.Sql.Tests`（2026-10-16 落地） |
| HTTP 客户端（URL scope 默认拒绝、重定向逐跳复检、内联/流式响应） | 已实现 | `Tarui.Plugins.Http/HttpService.cs`、`UrlScopeMatcher` |
| Log（renderer→MEL、`log://entry` 授权广播） | 已实现 | `Tarui.Plugins.Log`、`Tarui.Shell/RemoteLogSink.cs` |
| System（os/path/process exit+restart/shell open/clipboard 文本） | 已实现 | `Tarui.Plugins.System/SystemPlugin.cs` |
| Menu（窗口菜单 set/update-item/remove） | 已实现 | `Tarui.Plugins.Menu`、`Tarui.Shell/AvaloniaMenuService.cs` |
| Tray（create/set-menu/set-icon/set-tooltip/set-visible/remove） | 已实现 | `Tarui.Plugins.Tray`、`Tarui.Shell/AvaloniaTrayService.cs` |
| Notification（权限模型 + show/cancel + activated/dismissed 事件；Windows Toast：操作按钮 + 通知中心 + WinRT 生命周期事件，balloon 自动回退） | 已实现(Windows) | `Tarui.Shell.Toast/WindowsToastNotificationService.cs`（非 Windows 经 fallback balloon 诚实降级） |
| GlobalShortcut（register/unregister/is-registered、accelerator 归一化、scope glob） | 已实现(Windows) | `Tarui.Shell/WindowsGlobalShortcutService.cs`（非 Windows 降级） |
| Autostart（enable/disable/is-enabled） | 已实现 | `WindowsAutostartService`（HKCU Run）/ `MacAutostartService`（LaunchAgents plist）/ `LinuxAutostartService`（.desktop） |
| WindowState（save/restore/clear + 显示器拟合） | 已实现 | `Tarui.Plugins.WindowState`、`WindowStateFit.ClampToMonitors` |
| SingleInstance（Mutex + 命名管道/Unix socket 转发、`app://second-instance`） | 已实现(Windows 验证) | `Tarui.SingleInstance/SingleInstanceGuard.cs` |
| DeepLink（get-current + 事件；Windows cold/warm） | 部分实现 | `Tarui.Plugins.DeepLink`、`Tarui.Shell/DeepLinkService.cs`（macOS/Linux 待真机验收） |
| Updater（check/download + 签名验证 + SHA-256 + staging、apply、`updater://status`） | 已实现 | `Tarui.Shell/UpdaterService.cs`、`Tarui.Shell/UpdateApplier.cs` |
| CLI（init / plugin init+pack / info / dev / build） | 已实现 | `src/tarui-cli/Program.cs` |
| 前端 API（32 模块：ipc/app/window/webview/event/dialog/os/path/platform/process/shell/clipboard/fs/menu/tray/window-state/single-instance/notification/autostart/global-shortcut/store/sql/log/deep-link/updater/http/cli/cookie/positioner/websocket/persisted-scope） | 已实现 | `web/packages/api/index.ts` |
| Channel 流式 IPC | 已实现 | 端到端链路（Channel→sink→WebviewSession），`core:channel|stream-echo` 验证 |
| 测试 | **27 套控制台式自测试**（2026-10-16 实测全绿） | `tests/Tarui.*.Tests`（含 `Tarui.Http.Tests`、`Tarui.Sql.Tests`、`Tarui.PersistedScope.Tests`）；前端 `vitest` 49 例 |

## 3. 与 Tauri v2 对照

### 3.1 核心运行时能力

| Tauri v2 核心 | Tarui 状态 | 差距说明 |
| --- | --- | --- |
| invoke 命令调用 | ✅ | 等价（无反射 + 源生成元数据，语义一致） |
| Channel 流式数据 | ✅ | 端到端流式协议已接线；fs 大文件流式读/分片写与 HTTP 流式响应已解锁 |
| 多窗口 | ✅ | create/getAll/by label，能力对齐 |
| 单窗口多 webview | ❌ | **唯一架构级缺口**。Tauri 的 Window 与 Webview 是两个独立实体，一个窗口可挂多个 WebView；Tarui 从 `WindowOptions` 到 `IWebViewFactory` 为窗口↔WebView 强绑定的一对一模型。后果：「主窗口 + 侧边浮层 webview」「标签页多视图」「托盘气泡窗」等形态需绕开框架自建；`plugin:webview|*` 隐式绑定调用窗口，缺按 webview label 独立寻址。详见 §5 P0-0 |
| 窗口 API 全集 | 🟡 | ✅ 已补齐 setIcon/setTheme、Transparent（acrylic/transparent）、Parent/Modal（ShowDialog 模态 + Owner 关联）、deny-close 可取消关闭、unminimize、set-skip-taskbar、destroy（`Window.destroy()` 语义别名 = `close(force=true)`，不 emit close-requested）、setZoom/print；缺 always-on-bottom、setShadow、setBadgeCount/setProgressBar 等平台项 |
| 自定义协议 + CSP | ✅ | `tarui://localhost`、CSP、SPA fallback |
| 导航/下载策略 | ✅ | 策略引擎 + capability 双重授权，语义等价 |
| 事件 emit/listen（含 once、定向） | ✅ | 等价且多一层接收权限 |
| DevTools 开关 | ✅ | `plugin:webview|devtools`（Open/Close，CEF `ShowDevTools`，权限门控 + `-other-webview`） |
| Cookie 管理 | ✅ | `IWebViewCookieManager` + `Tarui.Plugins.Cookie`（list/set/remove/flush，CEF 全局存储；无宿主时诚实降级） |
| webview 崩溃/渲染进程终止事件 | ✅ | `webview://render-process-gone`（CEF OnRenderProcessTerminated → capability 门控 emit，status 映射 crashed/killed/other） |
| `eval_with_callback` | ✅ | `plugin:webview|eval-with-callback`（包装脚本保留完成值语义 + Promise 解析，经 Channel 回执） |
| 文件关联（file association） | ✅ | bundler `bundle.fileAssociations`（校验 + JSON Schema）→ MSIX `uap:fileTypeAssociation` + macOS `CFBundleDocumentTypes`（InfoPlistBuilder）；便携 zip 走运行时 `WindowsFileAssociationRegistrar`（HKCU ProgID + `.ext` 映射，`FileAssociationRegistrarHostedService`） |
| 窗口透明 | 🟡 | ✅ Transparent（Avalonia `TransparencyLevelHint` acrylic/transparent + 双背景刷透明）已封装；圆角/阴影（`setShadow`）未封装 |

### 3.2 官方插件矩阵（桌面相关）

| Tauri v2 官方能力 | Tarui 状态 | 说明 |
| --- | --- | --- |
| Autostart | ✅ (Windows/macOS/Linux) | Windows registry / macOS LaunchAgents plist / Linux `.desktop` 均已实现；平台感知 DI 选择 |
| Clipboard Manager | ✅ | readText/writeText + readHtml/writeHtml（含纯文本回退）+ readImage/writeImage（PNG 字节） |
| CLI（结构化参数解析） | ✅ | `core:cli|parse`：声明 `--long`/`-x` 选项、位置参数、多值、必需校验与类型解析 |
| Deep Linking | 🟡 | Windows 全链路；macOS delegate / Linux .desktop 未验收 |
| Dialog | ✅ | open/save/message/confirm + ask（Yes/No 三态语义） |
| FileSystem | ✅ | 16 条命令 + scope + 流式读/分片写 + 大文件按块推送 + 目录 watch |
| Cookie | ✅ | list/set/remove/flush（`plugin:cookie|*`，CEF 全局存储），无浏览器宿主时诚实降级 |
| Global Shortcut | ✅ (Windows) | 含 accelerator 归一化与 scope，语义对齐 |
| HTTP Client | ✅ | URL scope 默认拒绝、重定向逐跳复检、内联/流式响应；`Tarui.Plugins.Http` |
| Localhost server | ✅ | WebAppOptions HTTP 模式等价 |
| Logging | ✅ | 双向桥接 + `log://entry`，等价 |
| Notifications | ✅ | ✅ Windows Toast 升级完成（2026-10-04）：`NotificationOptions.Actions`（≤5 个操作按钮）、驻留通知中心（AUMID 注册）、`notification://activated/dismissed` 事件经 WinRT `ToastNotification` 事件真实触发（按钮 id 映射 `NotificationEvent.Action`）；balloon 作为自动回退载体 |
| Opener | ✅ | shell open 等价 |
| OS Information | ✅ | 等价 |
| Persisted Scope | ✅ | `plugin:persisted-scope|scope-allow/deny/reset`（可扩展权限白名单硬编码）+ `scope://changed` 事件 + `appData/scope.json` 原子持久化；`IRuntimeScopeOverlay` 在 IPC 层与静态 capability scope 合并（仅扩展已声明 scope 的权限，deny 优先）；`tests/Tarui.PersistedScope.Tests` |
| Positioner | ✅ | `plugin:positioner|set-position`（8 屏幕锚位 + 6 Tray* 锚位诚实回退；`tests/Tarui.Positioner.Tests`） |
| Process | ✅ | exit/restart 走 Host 协调退出，等价 |
| Shell（子进程/sidecar） | ✅ | spawn + Channel 流式 stdio（stdout/stderr/`terminated`）+ 退出码 + 程序白名单作用域（默认拒绝） + stdin/kill + 进程树终止 |
| Single Instance | ✅ | Mutex + 管道/Unix socket + 参数转发事件 |
| SQL | ✅ | `plugin:sql|load/select/execute/close`（2026-10-16 落地）。SQLite 驱动内置（`Microsoft.Data.Sqlite` 10.0.13），连接串 scheme 决定驱动，`ISqlDriverResolver` 可显式注册 PostgreSQL/MySQL；`?` 位置参数经 `RewritePositionalPlaceholders` 改写为命名参数绑定（跳过字符串字面量与注释），值永不拼接进 SQL；cell 以「值 + 存储类 + IsNull」承载，区分 NULL/空串/整数/实数/blob（base64）；句柄按 `SemaphoreSlim` 串行化以规避 `SQLITE_BUSY`，`:memory:` 因随连接销毁而共享连接；**`load` 的 scope 语义比 fs/store 更严——空 allow 列表拒绝一切文件库**（load 会创建文件），须显式 allow 该数据库文件；路径经 `IFileAccessPolicy.Authorize` 拒绝根路径/逃逸。`tests/Tarui.Sql.Tests` 14 例 + 前端 9 例 |
| Store | ✅ | 等价（JSON KV + scope + 原子写） |
| Stronghold | ❌ | 未实现（加密存储，延后） |
| Updater | ✅ | check/download + ECDSA(P-384/SHA-384) 签名 + 逐文件 SHA-256 + 受控 staging + `updater://status` 事件；apply（Windows MSIX `Add-AppxPackage` + staging 根域校验 + apply 状态事件）已落地；macOS/Linux 走 `NoOpUpdateApplier` 显式声明 `update-apply-unsupported`，默认 capability 不授权 apply |
| Upload | ✅ | multipart/form-data 上传（`plugin:http|upload`，URL 作用域默认拒绝 + 重定向复检） |
| Websocket | ✅ | `plugin:websocket|connect/send/close`（`ClientWebSocket` + Channel 帧推送，URL scope 复用 `UrlScopeMatcher` 默认拒绝；TS `TaruiWebSocket`；`tests/Tarui.WebSocket.Tests`） |
| Window State | ✅ | 等价（含显示器拟合） |
| 移动端插件（barcode/biometric/geolocation/haptics/nfc） | — | 非目标（对齐计划 §1 明确排除） |

## 4. 与 Wails v3 对照

| Wails v3 能力 | Tarui 状态 | 差距说明 |
| --- | --- | --- |
| 多窗口（含生命周期、创建/销毁回调） | ✅ | 多窗口 + 事件 + owner 归属校验 |
| 父子窗口 + modal（macOS sheet） | ✅ | `WindowOptions.Parent`+`Modal`：父窗口存在性校验、`ShowDialog(parent)` 模态、`Show(parent)` Owner 关联（2026-10-04） |
| 系统托盘（图标、菜单、明暗自适应图标、窗口附着居中） | 🟡 | 图标/菜单/tooltip/事件已有；✅ Positioner 已落地（8 屏幕锚位 + 6 Tray* 锚位，Tray* 无原生任务栏矩形时诚实回退工作区底部槽位）；"点击托盘在图标旁精确弹出"待任务栏矩形原生途径补齐 |
| 原生菜单（菜单栏 + 上下文菜单） | ✅ | 窗口菜单 + 任意坐标 context menu popup（`menu://item-clicked` 路由） |
| 事件系统（应用/窗口事件 + RegisterHook 可取消钩子） | 🟡 | 事件 + 权限对齐；✅ close 拦截钩子已落地（`window://close-requested` + `core:window|deny-close` 回执 + 强制关闭回退定时器）；其余可取消钩子（move/resize 拦截）未实现 |
| Services 生命周期（ServiceStartup/Shutdown） | ✅ | .NET `IHostedService` + DI 生态等价且更强 |
| Bindings 自动生成 | 🟡 | C# 侧 Roslyn 源生成；TS 侧手写模块（30 个），与 Wails 自动生成 JS/TS 绑定的开发体验有差距 |
| 构建系统（wails3 build/task、打包分发） | 🟡 | `tarui dev/build` 已有；无安装包产物（NSIS/MSI）、图标/版本资源嵌入 |
| Dialogs（message/FileDialog/OpenDirectoryDialog） | ✅ | 等价 |
| 剪贴板 | ✅ | 文本 + HTML（含回退纯文本）+ 图片（PNG 字节） |
| 通知 | ✅ (Windows) | Windows Toast 载体（操作按钮 + 通知中心 + 生命周期事件），balloon 自动回退（Wails v3 停留在平台基础通知） |
| 全局快捷键 | ✅ (Windows) | 等价 |
| Screen API（屏幕列表、主屏、DPI） | ✅ | 显示器信息已有 |
| 拖放 + frameless + 拖拽区域 | ✅ | 策略化实现，含 NoDrag 覆盖 |
| DevTools 集成 | ✅ | `plugin:webview|devtools`（CEF `ShowDevTools/CloseDevTools`，权限门控 + `-other-webview` 变体；TS `Webview.openDevtools/closeDevtools`） |
| WML（Wails Markup Language） | — | 不建议跟进（专有 DSL，价值有限） |
| Linux GTK4/WebKitGTK 栈 | — | Tarui 走 Avalonia + CEF，窗口层天然跨平台；差异为技术栈选择而非缺口 |

## 5. 缺口清单（按优先级）

### P0 — 桌面框架核心缺口

| # | 缺口 | 对标 | 依赖/前置 |
| --- | --- | --- | --- |
| 0 | **单窗口多 WebView**（Window 与 Webview 解耦、webview label 独立寻址） | Tauri v2 `WebviewWindowBuilder` | 🟡 **当前唯一架构级缺口**，需先出设计稿：拆分 `WindowRegistry` 的窗口↔WebView 一对一假设、`ShellWindow` 的单 WebView 宿主模型、`IWebViewFactory` 的创建签名，并重定义 `plugin:webview|*` 的寻址语义。影响面覆盖窗口/权限/IPC/前端，属重构而非加插件，建议优先出设计再动手 |
| 1 | HTTP 客户端插件（受限 fetch：URL scope、流式响应、超时） | Tauri http-client | ✅ 已完成（`Tarui.Plugins.Http`） |
| 2 | Shell 子进程执行（spawn + stdio + 退出码 + sidecar） | Tauri shell | ✅ 已完成（`Tarui.Plugins.Shell`：程序白名单作用域默认拒绝 + Channel 流式 stdio + 退出码 + 进程树终止） |
| 3 | 上下文菜单（context menu popup，任意坐标弹出） | Tauri Menu::popup / Wails menus | ✅ 已完成（`plugin:menu|show-context-menu`，Popup + Menu 复用声明式 items + 点击路由） |
| 4 | Channel 端到端流式 IPC | Tauri ipc::Channel | ✅ 已完成（解锁 fs 大文件与 http 流式响应） |
| 5 | Updater apply（安装、替换、重启） | Tauri updater | ✅ 已完成（`plugin:updater|apply`：staged 定位 + Windows MSIX Add-AppxPackage + apply 状态事件；重启交由调用方） |
| 6 | 打包分发（安装包 NSIS/MSI、图标与版本资源嵌入） | Tauri bundler / wails3 build | ✅ 已完成（`tarui build` 产出 zip + 自研 MSIX 打包器 + 签名 latest.json，MSIX 即安装器分发形态） |

### P1 — 重要能力（常见应用需要，或有明确对标语义）

| # | 缺口 | 对标 | 说明 |
| --- | --- | --- | --- |
| 7 | Dialog `ask`（Yes/No 语义） | Tauri dialog | ✅ 已完成（`plugin:dialog|ask`，Yes/No 三态，显式 cancel 可选） |
| 8 | 剪贴板扩展（图片、HTML） | Tauri clipboard-manager | ✅ 已完成（`core:clipboard|read/write-html`、`read/write-image`；HTML 含纯文本回退，图片以 PNG 字节承载） |
| 9 | fs watch 目录监听 | Tauri fs | ✅ 已完成（`plugin:fs|watch`/`unwatch`：`FileSystemWatcher` + `fs://watch-change` 定向事件） |
| 10 | Cookie 管理 API | CEF 原生 | ✅ 已完成（`plugin:cookie|list|set|remove|flush`：`CefGlueCookieStore` 组件 + `Tarui.Plugins.Cookie`，无宿主时可读降级） |
| 11 | DevTools 开关 | 双方均有 | ✅ 已完成（`plugin:webview|devtools`：CEF `ShowDevTools/CloseDevTools`，webview 权限门控 + `-other-webview` 变体） |
| 12 | 窗口增强：setIcon、setTheme、transparent、modal/父子窗口 | Tauri window / Wails v3 | ✅ 已完成（`core:window|set-icon`、`set-theme`；`WindowOptions.Transparent` 映射 Avalonia acrylic/transparent；`Parent`+`Modal` 支持 ShowDialog 模态对话框与非模态 Owner 关联） |
| 13 | Positioner（托盘图标旁定位等预设位置） | Tauri positioner / Wails tray attach | ✅ 已完成（`plugin:positioner|set-position`：8 屏幕锚位纯函数计算 + 6 Tray* 锚位（无任务栏矩形原生途径时诚实回退工作区底部槽位）；`tests/Tarui.Positioner.Tests`） |
| 14 | macOS/Linux 平台补齐（notification、global-shortcut、deep-link 真机验收） | 双方均跨平台 | 🟡 进展：autostart 已三平台落地；`core:platform|capabilities` 能力矩阵已暴露；macOS deep-link `NSAppleEventManager` `kAEGetURL` 桥（`MacDeepLinkBridge` + 2s 去重窗口）已落地，真机验收待执行；notification / global-shortcut 仍待真机验收 |
| 15 | 结构化 CLI 参数解析插件 | Tauri cli | ✅ 已完成（`core:cli|parse`：声明式 `--long`/`-x` 选项 + 位置参数，flag/文本/多值/数字类型、必需校验，未知或类型错误诚实失败） |
| 16 | Web 侧可取消窗口事件钩子（onCloseRequested 拦截） | Tauri / Wails v3 RegisterHook | ✅ 已完成（2026-10-04。Shell 取消 OS close → emit `window://close-requested` + 强制关闭回退定时器；Web 回执 `core:window|deny-close`（非 ClosePending 时取消回退，幂等）；`Window.denyClose()` TS API；超时兜底强制关闭） |

### P2 — 按需增强（产品需求驱动，对标中为可选插件）

| # | 缺口 | 对标 |
| --- | --- | --- |
| 17 | SQL 插件（SQLite） | Tauri sql | ✅ 已完成（2026-10-16。`plugin:sql|load/select/execute/close`，权限按 `allow-load/allow-select/allow-execute/allow-close` 四分离；驱动经 `ISqlDriverResolver` 可扩展；`load` scope 显式要求 allow 条目；`tests/Tarui.Sql.Tests`） |
| 17b | SQL 非 SQLite 驱动（PostgreSQL / MySQL） | Tauri sql | ❌ 架构已预留（`AddSqlDriver<T>()`），未内置实现；Tauri 侧由 sqlx feature 开关提供，属按需增强 |
| 18 | WebSocket 插件 | Tauri websocket | ✅ 已完成（2026-10-04。`plugin:websocket|connect/send/close`：`ClientWebSocket` + Channel 帧推送（text/binary/closed），URL scope 复用 `UrlScopeMatcher` 默认拒绝，`ws/wss` 默认端口语义；TS `TaruiWebSocket`；`tests/Tarui.WebSocket.Tests` 含真实 HttpListener 回环握手） |
| 19 | Upload（multipart） | Tauri upload（依赖 http） | ✅ 已完成（`plugin:http|upload`：multipart/form-data 上传，URL scope 默认拒绝 + 重定向逐跳复检 + inline 上限）。注：HTTP 流式（`plugin:http|fetch` + `onEvent`）与 fs 流式读已落地（见 §6.2） |
| 20 | Persisted Scope（运行时 scope 变更持久化） | Tauri persisted-scope | ✅ 已完成（2026-10-04。`plugin:persisted-scope|scope-allow/deny/reset` + `scope://changed` 事件 + `appData/scope.json` 原子持久化；`IRuntimeScopeOverlay` 在 IPC 层与静态 capability scope 合并（deny 优先）；`tests/Tarui.PersistedScope.Tests`） |
| 21 | Stronghold 类加密存储 | Tauri stronghold |
| 22 | 单窗口多 webview | Tauri v2 |
| 23 | 文件关联 | Tauri v2.11 | ✅ 已完成（2026-10-04。bundler `bundle.fileAssociations`（校验 + JSON Schema）→ MSIX `uap:fileTypeAssociation` + macOS `CFBundleDocumentTypes`（InfoPlistBuilder）；便携 zip 走运行时 `WindowsFileAssociationRegistrar`（HKCU ProgID + `.ext` 映射，`FileAssociationRegistrarHostedService`）） |
| 24 | UserAgent/Proxy 等 WebView 运行时配置暴露 | CEF 原生 | ✅ 已完成（`CefGlueNextAvaloniaRuntimeOptions.UserAgent/ProxyServer` → `CefSettings.UserAgent` + `proxy-server` 命令行开关，经 `TARUI_WEB_USER_AGENT`/`TARUI_WEB_PROXY_SERVER` 配置；CEF 仅支持初始化期配置） |

## 6. 现有实现可优化项

以下针对**已实现功能**的改进，不新增能力面：

1. **通知载体升级（高价值）**：✅ 已完成（2026-10-04）。`Tarui.Shell.Toast/WindowsToastNotificationService`（双 TFM + WinRT 引擎条件编译）以 Windows Toast 取代 balloon 载体：操作按钮（`NotificationOptions.Actions` ≤5）、驻留通知中心（HKCU AUMID 注册）、`notification://activated/dismissed` 经 WinRT 生命周期事件真实触发（按钮 arguments 映射 `NotificationEvent.Action`）；引擎缺失或 Show 失败时自动降级 balloon fallback（去重语义保留）。
2. **fs 大文件上限解耦**：✅ 已完成（2026-09-19）。8 MiB 文本单次上限已通过 Channel 流式读/分片写解耦：`plugin:fs|read-file-stream`（meta + chunk 帧）+ `plugin:fs|write-begin|chunk|commit|cancel`（原子替换语义保留）；`read-text-file` 仍为小文件便利 API，大文件走流式。`Tarui.Ipc.Tests` 4 组用例覆盖预算、sink、原子提交、cancel 与乱序拒绝。
3. **平台可用性元数据**（高价值）✅：`core:platform|capabilities` + `@lytree/api/platform` 已在握手期暴露 notification/global-shortcut/autostart/deep-link 的真实可用性矩阵，前端据此禁用不可用 UI；通知与全局快捷键的非 Windows 平台能力仍为诚实降级并由该矩阵如实反映。
4. **TS API 代码生成**：32 个手写模块与 C# 契约存在双维护成本；扩展现有 Roslyn 生成器或 CLI（`tarui plugin pack` 流程）从 `TaruiJsonContext` DTO 生成 TS 类型和 invoke 封装，对齐 Wails bindings 开发体验，消除漂移风险。
5. **菜单局部更新**：✅ 已完成（2026-10-04）。`plugin:menu|append`（根级末尾追加）/`insert`（根级指定位置插入）/`remove`（按 id 深度搜索移除）；复用"定义树 → 整树重建 NativeMenu"模式，新增项与现有树做全局唯一 id 校验；TS `menu.appendItems/insertItems/removeItem`。
6. **事件广播扇出**：`EventRouter` 定向/广播按窗口遍历投递；多窗口高频事件（如日志流 `log://entry`）下建议评估批量编码或共享序列化快照，避免逐窗口重复序列化。
7. **IPC 载体评估**：当前 JSON 源生成；对标 Tauri v2 的 raw/JSON 双通道，Channel 流式落地时可一并评估二进制帧格式（仍走源生成元数据，不引入反射）。
8. **对齐计划文档状态同步**：✅ 已完成（2026-09-19）。`tauri-desktop-alignment-plan.md §15` 状态表已按本文与代码同步刷新：DeepLink/Updater/Shell/HTTP/Cookie/CLI 等 Phase 6 子项均已登记证据；Phase 0–4 列为"已完成（Windows 已验证）"，Phase 5 列为"已完成（待真机 windowed CEF 验收）"，Phase 6 列为"已完成（带缺口）"——Updater apply 仍受"签名 PKI + 升级服务器 + 安装器策略"前置门禁，由 capability 默认不授权控制；macOS/Linux 仅代码已落，真机运行证据待补。
9. **CI 平台矩阵**：27 套自测试仅在 Windows 运行；macOS/Linux 至少应跑无 UI 的策略/契约/权限类测试（WebViewEvents、Capabilities、Ipc、Http、Sql 等），缩小"未验证"范围。
10. **Dev/Build 开发体验**：`tarui dev` 现为进程编排级；对标 Wails v3 task 体系，后续可补前端 HMR 与 .NET 热重载（dotnet watch）联动、以及 `tarui build` 的一键产物链。

## 7. 建议推进顺序

衔接 [tauri-desktop-alignment-plan.md](./tauri-desktop-alignment-plan.md) 的既有阶段（其 §14 顺序已执行至第 10 步），后续建议：

1. **Channel 端到端流式 IPC**（P0-4）——✅ 已完成（`core:channel|stream-echo` 全链路验证；解锁 fs/HTTP 流式 + Shell stdio）。
2. **HTTP 客户端 + Shell 子进程插件**（P0-1/2）——✅ 已完成：HTTP 客户端（URL scope + 重定向逐跳复检 + 内联/流式双模式 + Upload）；Shell 子进程（程序白名单作用域默认拒绝 + Channel 流式 stdio + 退出码 + kill）。
3. **上下文菜单 + Dialog ask**（P0-3、P1-7）——✅ 已完成（`plugin:menu|show-context-menu` + `plugin:dialog|ask`）。
4. **Updater apply + 打包分发**（P0-5/6）——✅ 代码已落地：apply 走 MSIX `Add-AppxPackage` + staging 根域校验 + 状态事件；打包分发为现有 `tarui build` zip/MSIX/`.app.tar.gz`/签名 latest.json，重启由调用方衔接 process relaunch，与 OIDC 发布工作流衔接。macOS 真机构建管道（`ci-macos.yml` + `release.yml#pack-and-build-macos`）见 [docs/adr/0002-macos-real-build-pipeline.md](../adr/0002-macos-real-build-pipeline.md)。**前置门禁**：apply 的 capability 默认不授权，需 PKI + 升级服务器 + 安装器策略齐备后由能力清单显式开启；macOS `.app` 未签名/未公证，分发前需 `codesign --deep` + `notarytool`（ADR-0002 §8）。
5. **平台补齐（macOS/Linux）**——🟡 进展：autostart 已三平台落地，`core:platform|capabilities` 能力矩阵已暴露；macOS deep-link `NSAppleEventManager` `kAEGetURL` 桥已落地（`MacDeepLinkBridge` + 2s 去重），真机验收待执行；notification / global-shortcut 仍需 macOS/Linux 真机验收后补齐。
6. **P2 项按产品需求排期**——✅ 已落地：Upload（multipart）、WebView 运行时配置（UserAgent/Proxy）、fs watch、Cookie API、DevTools 开关、剪贴板扩展（HTML/图片）、结构化 CLI 解析、WebSocket、Positioner、文件关联、Persisted Scope（均 2026-10-04）、**SQL（2026-10-16）**。剩余：**Stronghold**（加密存储）、**单窗口多 WebView**（架构级，见 P0-0）、SQL 非 SQLite 驱动。
7. **窗口增强 + Positioner**——✅ 已完成（2026-10-04）：P1-12 `Transparent`/`Parent`/`Modal`（ShowDialog 模态与非模态 Owner 关联）；P1-13 Positioner 插件（8 屏幕锚位 + Tray* 回退）。
8. **可取消窗口事件钩子**（P1-16）——✅ 已完成（2026-10-04）：`core:window|deny-close` 回执通道 + 强制关闭回退定时器 + 超时兜底。附带完成 `plugin:webview|eval`/`eval-with-callback`（对标 `eval_with_callback`，经 Channel 回执完成值）与 `webview://render-process-gone` 渲染进程崩溃事件。
9. **CI 平台矩阵扩大**（§6-9）：当前 27 套自测试只跑 Windows；至少应在 macOS/Linux 跑无 UI 的策略/契约/权限类（WebViewEvents、Capabilities、Ipc、Http、Sql 等），缩小"未验证"范围。
10. **Phase 5 真机 windowed CEF 验收**：文件进入/离开/放下、多窗口拖放命中、拖拽区域动态更新、下载/导航外部打开、WebView 反复创建销毁无残留——以真实运行证据替代"代码与门禁"。
11. **单窗口多 WebView 设计稿（P0-0，当前最高优先）**：先出设计再动手，需覆盖 ①窗口与 WebView 的注册与生命周期解耦；②`plugin:webview|*` 按 webview label 寻址的语义与权限边界；③多 WebView 共享窗口事件与 capability 的隔离规则；④前端 `webview.ts` 与窗口 API 的对应关系调整。

每项仍须遵循既有模板：`Contracts DTO → 显式插件注册 → Shell/平台实现 → Capability 授权 → @lytree/api 模块 → 控制台式自测试 → 文档同步`。

## 8. 参考

- Tauri v2 Features & Recipes（官方插件矩阵）：<https://v2.tauri.app/plugin/>
- Tauri 生态版本与发布说明（本次基线 tauri 2.12.3）：<https://v2.tauri.app/release/>
- Wails v3 What's New（多窗口/托盘/事件/WML）：<https://v3.wails.io/whats-new/>
- Wails v3 多窗口与生命周期：<https://v3.wails.io/features/windows/multiple/>、<https://v3.wails.io/concepts/lifecycle/>
- 仓库内基线：[tauri-desktop-alignment-plan.md](./alignment-plan.md)（2026-08-21 基线，本文为其增量盘点）
