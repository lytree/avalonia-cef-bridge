# Tarui 与 Wails v3 / Tauri v2 功能差距分析

> 状态：分析基线
> 基线日期：2026-09-04
> 对比对象：Tauri v2 官方插件与核心 API、Wails v3（alpha）
> 姊妹文档：[tauri-desktop-alignment-plan.md](./tauri-desktop-alignment-plan.md)（实施步骤与状态跟踪的权威入口，本文不重复其阶段门禁）
> 本文目的：静态盘点当前仓库实际能力，对照两个对标框架输出缺口清单与可优化项，供排期决策。

## 1. 结论摘要

- Tarui 已完成桌面壳层核心：无反射 IPC、多窗口、CEF 150 WebView（自定义协议/导航与下载策略/文件拖放）、Capability v2 权限模型、Channel 端到端流式 IPC（含 fs 大文件流式读写与 HTTP 流式响应、Shell 子进程 stdio 流）、25 套自测试、25 个前端 API 模块。
- Tauri v2 桌面相关 25 个官方能力中：已对齐约 18 个（多为 Windows 验证）、部分对齐 5 个、缺失 3 个（sql、stronghold、persisted-scope 等）。
- Wails v3 桌面能力（多窗口/托盘/菜单/事件/对话框/拖放/frameless）基本具备等价实现；显著差距在开发体验（bindings 自动生成、可取消窗口事件钩子）与托盘窗口附着定位。
- **最高优先缺口（P0）**：已全部落地（Channel 流式 IPC、fs 大文件、HTTP 客户端、Shell 子进程、上下文菜单、Dialog ask、Updater apply + 打包分发）。剩余聚焦**平台补齐**（autostart 三平台已落地，notification/global-shortcut/macOS deep-link 待真机验收）与 P2 按需增强。
- 平台现实：所有原生能力当前仅 Windows 完成验证；autostart 已有 macOS/Linux 实现，notification/global-shortcut 在非 Windows 为诚实降级并通过 `core:platform|capabilities` 如实暴露。

## 2. Tarui 当前能力基线（2026-09-04 快照）

| 能力域 | 状态 | 关键落点 |
| --- | --- | --- |
| IPC（无反射、源生成元数据） | 已实现 | `Tarui.Ipc/CommandRouter.cs`、`Tarui.Contracts` DTO + `TaruiJsonContext` |
| Capability v2（allow/deny scope、glob、事件接收授权） | 已实现 | `Tarui.Ipc/CapabilitySet.cs`、`capabilities/*.json` |
| 多窗口管理（创建/显隐/焦点/置顶/全屏/去装饰/显示器） | 已实现 | `Tarui.Shell/AvaloniaWindowService.cs`、`Tarui.Plugins.Window` |
| WebView（CEF 150.x、`tarui://` 自定义协议、HTTP 模式、CSP、SPA fallback） | 已实现 | `Tarui.WebView.CefGlueNext/CefGlueNextWebViewFactory.cs`、`CefGlueNextWebAppOptions.cs` |
| 导航/下载策略（allow/external/deny glob、host allow 默认 deny） | 已实现 | `Tarui.WebView.Abstractions/WebViewRequestPolicy.cs` |
| 文件拖放 + 拖拽区域（NoDrag 覆盖、差异比较） | 已实现 | `DraggableRegion.cs`、`CefGlueNextNativeHandlers.cs`、`webview://*`、`window://file-drop-*` 事件 |
| 事件系统（emit 定向/广播、`user://` 前缀保护、原生前缀保留） | 已实现 | `Tarui.Shell/EventRouter.cs`、`Tarui.Plugins.Event` |
| Dialog（open/save/message/confirm） | 已实现 | `Tarui.Plugins.Dialog`、`Tarui.Shell/AvaloniaDialogService.cs` |
| FileSystem（14 条命令、scoped glob、原子写、流式读/分片写、大文件按块推送） | 已实现 | `Tarui.Plugins.FileSystem/FileSystemService.cs`、`FsScopeAuthorizer` |
| Store（JSON KV、scope、原子写） | 已实现 | `Tarui.Plugins.Store/JsonStoreService.cs` |
| HTTP 客户端（URL scope 默认拒绝、重定向逐跳复检、内联/流式响应） | 已实现 | `Tarui.Plugins.Http/HttpService.cs`、`UrlScopeMatcher` |
| Log（renderer→MEL、`log://entry` 授权广播） | 已实现 | `Tarui.Plugins.Log`、`Tarui.Shell/RemoteLogSink.cs` |
| System（os/path/process exit+restart/shell open/clipboard 文本） | 已实现 | `Tarui.Plugins.System/SystemPlugin.cs` |
| Menu（窗口菜单 set/update-item/remove） | 已实现 | `Tarui.Plugins.Menu`、`Tarui.Shell/AvaloniaMenuService.cs` |
| Tray（create/set-menu/set-icon/set-tooltip/set-visible/remove） | 已实现 | `Tarui.Plugins.Tray`、`Tarui.Shell/AvaloniaTrayService.cs` |
| Notification（权限模型 + show/cancel + activated/dismissed 事件；Windows balloon） | 已实现(Windows) | `Tarui.Shell/WindowsNotificationService.cs`（非 Windows no-op） |
| GlobalShortcut（register/unregister/is-registered、accelerator 归一化、scope glob） | 已实现(Windows) | `Tarui.Shell/WindowsGlobalShortcutService.cs`（非 Windows 降级） |
| Autostart（enable/disable/is-enabled） | 已实现 | `WindowsAutostartService`（HKCU Run）/ `MacAutostartService`（LaunchAgents plist）/ `LinuxAutostartService`（.desktop） |
| WindowState（save/restore/clear + 显示器拟合） | 已实现 | `Tarui.Plugins.WindowState`、`WindowStateFit.ClampToMonitors` |
| SingleInstance（Mutex + 命名管道/Unix socket 转发、`app://second-instance`） | 已实现(Windows 验证) | `Tarui.SingleInstance/SingleInstanceGuard.cs` |
| DeepLink（get-current + 事件；Windows cold/warm） | 部分实现 | `Tarui.Plugins.DeepLink`、`Tarui.Shell/DeepLinkService.cs`（macOS/Linux 待真机验收） |
| Updater（check/download + 签名验证 + SHA-256 + staging、apply、`updater://status`） | 已实现 | `Tarui.Shell/UpdaterService.cs`、`Tarui.Shell/UpdateApplier.cs` |
| CLI（init / plugin init+pack / info / dev / build） | 已实现 | `src/tarui-cli/Program.cs` |
| 前端 API（25 模块：ipc/app/window/webview/event/dialog/os/path/platform/process/shell/clipboard/fs/menu/tray/window-state/single-instance/notification/autostart/global-shortcut/store/log/deep-link/updater/http） | 已实现 | `web/packages/api/index.ts` |
| Channel 流式 IPC | 已实现 | 端到端链路（Channel→sink→WebviewSession），`core:channel|stream-echo` 验证 |
| 测试 | 25 套控制台式自测试 | `tests/Tarui.*.Tests`（含 `Tarui.Http.Tests`、`Tarui.ShellPlugin.Tests`） |

## 3. 与 Tauri v2 对照

### 3.1 核心运行时能力

| Tauri v2 核心 | Tarui 状态 | 差距说明 |
| --- | --- | --- |
| invoke 命令调用 | ✅ | 等价（无反射 + 源生成元数据，语义一致） |
| Channel 流式数据 | ✅ | 端到端流式协议已接线；fs 大文件流式读/分片写与 HTTP 流式响应已解锁 |
| 多窗口 | ✅ | create/getAll/by label，能力对齐 |
| 单窗口多 webview | ❌ | Tarui 为窗口↔webview 一对一模型 |
| 窗口 API 全集 | 🟡 | 缺 setIcon、setTheme、透明/acrylic 模糊、modal 对话框窗口、父子窗口关联 |
| 自定义协议 + CSP | ✅ | `tarui://localhost`、CSP、SPA fallback |
| 导航/下载策略 | ✅ | 策略引擎 + capability 双重授权，语义等价 |
| 事件 emit/listen（含 once、定向） | ✅ | 等价且多一层接收权限 |
| DevTools 开关 | ❌ | CEF 本身支持，未暴露 API（仅 URL 策略黑名单中提及） |
| Cookie 管理 | ❌ | `CefGlue.Core` 已有 `CefCookieManager` 封装，未暴露为插件 |
| webview 崩溃/渲染进程终止事件 | 🟡 | CEF 事件模型具备，未见对外投递 |
| `eval_with_callback` | ❌ | 有 `ExecuteScriptAsync`，无回调形式 |
| 文件关联（file association） | ❌ | 未实现 |
| 窗口阴影/圆角/透明 | ❌ | Avalonia `TransparencyLevelHint` 未封装 |

### 3.2 官方插件矩阵（桌面相关）

| Tauri v2 官方能力 | Tarui 状态 | 说明 |
| --- | --- | --- |
| Autostart | ✅ (Windows/macOS/Linux) | Windows registry / macOS LaunchAgents plist / Linux `.desktop` 均已实现；平台感知 DI 选择 |
| Clipboard Manager | 🟡 | 仅 readText/writeText；缺图片、HTML、文件列表、clear |
| CLI（结构化参数解析） | ❌ | 只有原始 process args |
| Deep Linking | 🟡 | Windows 全链路；macOS delegate / Linux .desktop 未验收 |
| Dialog | ✅ | open/save/message/confirm + ask（Yes/No 三态语义） |
| File System | 🟡 | 14 条命令 + scope + 流式读/分片写；缺目录监听 watch |
| Global Shortcut | ✅ (Windows) | 含 accelerator 归一化与 scope，语义对齐 |
| HTTP Client | ✅ | URL scope 默认拒绝、重定向逐跳复检、内联/流式响应；`Tarui.Plugins.Http` |
| Localhost server | ✅ | WebAppOptions HTTP 模式等价 |
| Logging | ✅ | 双向桥接 + `log://entry`，等价 |
| Notifications | 🟡 | 权限模型/事件对齐；载体是 balloon tips，非现代 Toast（无操作按钮、不进通知中心） |
| Opener | ✅ | shell open 等价 |
| OS Information | ✅ | 等价 |
| Persisted Scope | ❌ | 未实现（运行时动态 scope 持久化） |
| Positioner | ❌ | 无窗口预设定位（托盘附着弹窗的核心依赖） |
| Process | ✅ | exit/restart 走 Host 协调退出，等价 |
| Shell（子进程/sidecar） | ✅ | spawn + stdio 流式回传 + 退出码 + 程序白名单作用域（默认拒绝）|
| Single Instance | ✅ | Mutex + 管道/Unix socket + 参数转发事件 |
| SQL | ❌ | 未实现（按产品需求延后） |
| Store | ✅ | 等价（JSON KV + scope + 原子写） |
| Stronghold | ❌ | 未实现（加密存储，延后） |
| Updater | ✅ | check/download + 签名 + 哈希 + staging 完整；apply（MSIX 安装 + staging 定位）已落地 |
| Upload | ✅ | multipart/form-data 上传（`plugin:http|upload`，URL 作用域默认拒绝 + 重定向复检） |
| Websocket | ❌ | 未实现 |
| Window State | ✅ | 等价（含显示器拟合） |
| 移动端插件（barcode/biometric/geolocation/haptics/nfc） | — | 非目标（对齐计划 §1 明确排除） |

## 4. 与 Wails v3 对照

| Wails v3 能力 | Tarui 状态 | 差距说明 |
| --- | --- | --- |
| 多窗口（含生命周期、创建/销毁回调） | ✅ | 多窗口 + 事件 + owner 归属校验 |
| 父子窗口 + modal（macOS sheet） | ❌ | 无 parent/child 关联与模态语义 |
| 系统托盘（图标、菜单、明暗自适应图标、窗口附着居中） | 🟡 | 图标/菜单/tooltip/事件已有；无"点击托盘在图标旁弹出窗口"（依赖 Positioner 类能力与 `HiddenOnTaskbar` 类选项） |
| 原生菜单（菜单栏 + 上下文菜单） | ✅ | 窗口菜单 + 任意坐标 context menu popup（`menu://item-clicked` 路由） |
| 事件系统（应用/窗口事件 + RegisterHook 可取消钩子） | 🟡 | 事件 + 权限对齐；无 Web 侧可取消的窗口事件钩子（如拦截 close） |
| Services 生命周期（ServiceStartup/Shutdown） | ✅ | .NET `IHostedService` + DI 生态等价且更强 |
| Bindings 自动生成 | 🟡 | C# 侧 Roslyn 源生成；TS 侧手写模块（23 个），与 Wails 自动生成 JS/TS 绑定的开发体验有差距 |
| 构建系统（wails3 build/task、打包分发） | 🟡 | `tarui dev/build` 已有；无安装包产物（NSIS/MSI）、图标/版本资源嵌入 |
| Dialogs（message/FileDialog/OpenDirectoryDialog） | ✅ | 等价 |
| 剪贴板 | ✅ | 文本读写等价 |
| 通知 | ✅ (Windows) | balloon 实现（Wails v3 同样基于平台通知，Tarui 载体偏旧） |
| 全局快捷键 | ✅ (Windows) | 等价 |
| Screen API（屏幕列表、主屏、DPI） | ✅ | 显示器信息已有 |
| 拖放 + frameless + 拖拽区域 | ✅ | 策略化实现，含 NoDrag 覆盖 |
| DevTools 集成 | 🟡 | CEF 支持但未暴露 |
| WML（Wails Markup Language） | — | 不建议跟进（专有 DSL，价值有限） |
| Linux GTK4/WebKitGTK 栈 | — | Tarui 走 Avalonia + CEF，窗口层天然跨平台；差异为技术栈选择而非缺口 |

## 5. 缺口清单（按优先级）

### P0 — 桌面框架核心缺口（两个对标框架均具备，阻塞典型应用形态）

| # | 缺口 | 对标 | 依赖/前置 |
| --- | --- | --- | --- |
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
| 8 | 剪贴板扩展（图片、HTML、clear） | Tauri clipboard-manager | Avalonia/CEF 均可承载 |
| 9 | fs watch 目录监听 | Tauri fs | `FileSystemWatcher` + 事件投递 |
| 10 | Cookie 管理 API | CEF 原生 | `CefGlue.Core/CefCookieManager` 已有底层封装，仅缺插件暴露 |
| 11 | DevTools 开关 | 双方均有 | CEF `ShowDevTools`，需权限门控 |
| 12 | 窗口增强：setIcon、setTheme、transparent/acrylic、modal、父子窗口 | Tauri window / Wails v3 | Avalonia 原语均支持，逐项封装 |
| 13 | Positioner（托盘图标旁定位等预设位置） | Tauri positioner / Wails tray attach | 托盘应用标准场景 |
| 14 | macOS/Linux 平台补齐（notification、global-shortcut、autostart、deep-link 真机验收） | 双方均跨平台 | 🟡 进展：autostart 已三平台落地；`core:platform|capabilities` 能力矩阵已暴露；notification / global-shortcut / macOS deep-link 仍待真机验收 |
| 15 | 结构化 CLI 参数解析插件 | Tauri cli | 可先以 System 插件扩展 |
| 16 | Web 侧可取消窗口事件钩子（onCloseRequested 拦截） | Tauri / Wails v3 RegisterHook | 事件系统已有，补回执通道 |

### P2 — 按需增强（产品需求驱动，对标中为可选插件）

| # | 缺口 | 对标 |
| --- | --- | --- |
| 17 | SQL 插件（SQLite） | Tauri sql |
| 18 | WebSocket 插件 | Tauri websocket |
| 19 | Upload（multipart） | Tauri upload（依赖 http） | ✅ 已完成（`plugin:http|upload`：multipart/form-data 上传，URL scope 默认拒绝 + 重定向逐跳复检 + inline 上限） |
| 20 | Persisted Scope（运行时 scope 变更持久化） | Tauri persisted-scope |
| 21 | Stronghold 类加密存储 | Tauri stronghold |
| 22 | 单窗口多 webview | Tauri v2 |
| 23 | 文件关联 | Tauri v2.11 |
| 24 | UserAgent/Proxy 等 WebView 运行时配置暴露 | CEF 原生 | ✅ 已完成（`CefGlueNextAvaloniaRuntimeOptions.UserAgent/ProxyServer` → `CefSettings.UserAgent` + `proxy-server` 命令行开关，经 `TARUI_WEB_USER_AGENT`/`TARUI_WEB_PROXY_SERVER` 配置；CEF 仅支持初始化期配置） |

## 6. 现有实现可优化项

以下针对**已实现功能**的改进，不新增能力面：

1. **通知载体升级（高价值）**：`WindowsNotificationService` 当前为 `Shell_NotifyIcon` balloon tips；升级为 Windows Toast 通知可获得操作按钮、驻留通知中心、与已有 `notification://activated/dismissed` 事件真正联动。balloon 载体下 activated 语义基本不可用。
2. **fs 大文件上限解耦**：8 MiB 文本单次上限已通过 Channel 流式读/分片写解耦（P0-4 落地时同步完成）；`read-text-file` 保留小文件便利上限，大文件走 `read-file-stream`/`write-begin|chunk|commit|cancel`。
3. **平台可用性元数据**（高价值）✅：`core:platform|capabilities` + `@lytree/api/platform` 已在握手期暴露 notification/global-shortcut/autostart/deep-link 的真实可用性矩阵，前端据此禁用不可用 UI；通知与全局快捷键的非 Windows 平台能力仍为诚实降级并由该矩阵如实反映。
4. **TS API 代码生成**：24 个手写模块与 C# 契约存在双维护成本；扩展现有 Roslyn 生成器或 CLI（`tarui plugin pack` 流程）从 `TaruiJsonContext` DTO 生成 TS 类型和 invoke 封装，对齐 Wails bindings 开发体验，消除漂移风险。
5. **菜单局部更新**：Menu 插件目前仅整树 set + 单项 update-item；可补充 append/insert/remove 级增量操作，避免大菜单整树重建（Avalonia `NativeMenu` 支持增量子项操作）。
6. **事件广播扇出**：`EventRouter` 定向/广播按窗口遍历投递；多窗口高频事件（如日志流 `log://entry`）下建议评估批量编码或共享序列化快照，避免逐窗口重复序列化。
7. **IPC 载体评估**：当前 JSON 源生成；对标 Tauri v2 的 raw/JSON 双通道，Channel 流式落地时可一并评估二进制帧格式（仍走源生成元数据，不引入反射）。
8. **对齐计划文档状态滞后**：`tauri-desktop-alignment-plan.md` 状态表中 DeepLink/Updater 尚未登记（代码与测试已存在：`Tarui.DeepLink.Tests`、`Tarui.Updater.Tests`），Phase 5/6 部分条目已落地但标"进行中"；建议按该文档 §15 状态更新规则补录证据，保持"权威状态入口"有效。
9. **CI 平台矩阵**：24 套自测试仅在 Windows 运行；macOS/Linux 至少应跑无 UI 的策略/契约/权限类测试（WebViewEvents、Capabilities、Ipc、Http 等），缩小"未验证"范围。
10. **Dev/Build 开发体验**：`tarui dev` 现为进程编排级；对标 Wails v3 task 体系，后续可补前端 HMR 与 .NET 热重载（dotnet watch）联动、以及 `tarui build` 的一键产物链。

## 7. 建议推进顺序

衔接 [tauri-desktop-alignment-plan.md](./tauri-desktop-alignment-plan.md) 的既有阶段（其 §14 顺序已执行至第 10 步），后续建议：

1. **Channel 端到端流式 IPC**（P0-4）——✅ 已完成（`core:channel|stream-echo` 全链路验证）。
2. **HTTP 客户端 + Shell 子进程插件**（P0-1/2）——HTTP 客户端已完成（URL scope + 流式响应）；Shell 子进程已完成（程序白名单作用域默认拒绝 + stdio 流式 + 退出码 + kill）。
3. **上下文菜单 + Dialog ask**（P0-3、P1-7）——✅ 已完成（`plugin:menu|show-context-menu` + `plugin:dialog|ask`）。
4. **Updater apply + 打包分发**（P0-5/6）——✅ 已完成（apply 落地 MSIX 安装 + 状态事件；打包分发为现有 `tarui build` zip/MSIX/签名 latest.json，重启由调用方衔接 process relaunch，与 OIDC 发布工作流衔接）。
5. **平台补齐（macOS/Linux）**——🟡 进展：autostart 已三平台落地，`core:platform|capabilities` 能力矩阵已暴露；notification / global-shortcut / macOS deep-link 需 macOS/Linux 真机验收后补齐。
6. **P2 项按产品需求排期**（fs watch、Cookie API、DevTools 开关、剪贴板扩展、结构化 CLI 解析等）——Upload（multipart）与 WebView 运行时配置（UserAgent/Proxy）已落地。

每项仍须遵循既有模板：`Contracts DTO → 显式插件注册 → Shell/平台实现 → Capability 授权 → @lytree/api 模块 → 控制台式自测试 → 文档同步`。

## 8. 参考

- Tauri v2 Features & Recipes（官方插件矩阵）：<https://v2.tauri.app/plugin/>
- Tauri v2 核心与发布说明：<https://tauri.app/release/tauri/v2.11.0/>
- Wails v3 What's New（多窗口/托盘/事件/WML）：<https://v3.wails.io/whats-new/>
- Wails v3 多窗口与生命周期：<https://v3.wails.io/features/windows/multiple/>、<https://v3.wails.io/concepts/lifecycle/>
- 仓库内基线：[tauri-desktop-alignment-plan.md](./tauri-desktop-alignment-plan.md)（2026-08-21 基线，本文为其增量盘点）
