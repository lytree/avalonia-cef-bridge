# Tarui 与 Tauri v3 差异分析与改造方案

> 状态：分析基线 + 改造方案；**V3-0（运行时身份）、V3-1（单窗口多 WebView）、V3-2（权限集合与清单精简）、V3-3（DX：CDP 通道 + 类型化契约）、V3-4（PC 平台一比一补齐：运行时扩展表面 / 原生资源目录 / 契约全量）均已实施完成**；PC 平台已无待排期差异项，剩余为上游主导的观察项
> 基线日期：2026-10-17（V3-2 实施于 2026-10-20，V3-4 实施于 2026-10-20）
> 对比对象：Tauri **3.0.0-alpha.0 … alpha.5**（2026-09-13 至 2026-10-14，GitHub Releases `tauri-v3.0.0-alpha.*`；稳定线为 2.12.4）、`tauri-runtime-wry` / `tauri-runtime-cef` 3.0.0-alpha、`@tauri-apps/cli` 3.0.0-alpha.4/5
> 平台范围：本文只对 **PC（Windows / macOS / Linux）** 求一比一；移动端（Android / iOS）为对齐计划 §1 明确非目标，D15 因此不排期
> 姊妹文档：[`alignment-plan.md`](alignment-plan.md)（v2 对齐实施，继续有效）、[`gap-analysis.md`](gap-analysis.md)（v2 / Wails v3 缺口；其 §5 P0-0 与本文 V3-1 对应）、[`webview.md`](webview.md)、[`web-resource-mode.md`](web-resource-mode.md)
> 本文目的：盘点 Tauri v3 已公布的架构变化，逐项评估与 Tarui 现状的差异，给出分阶段改造方案。v3 仍处 alpha（约 1–2 周一个 alpha），本文结论随新版本发布复核更新。

## 1. Tauri v3 主题概览

从 alpha.0–alpha.5 的 changelog 提炼，v3 的核心变化集中在八条主线：

| # | 主题 | 关键变化 | 来源 |
| --- | --- | --- | --- |
| T1 | **运行时解耦** | webview 运行时改为构建期显式选择：应用直接依赖运行时 crate 并 `Builder::runtime(Wry::default())`；`tauri` 核心默认使用类型擦除的 `DynRuntime`（未选运行时报 `RuntimeNotConfigured`）；`devtools`/`unstable`/`macos-private-api` 等 feature 移到运行时 crate | alpha.0 #15985 |
| T2 | **运行时特有表面隔离** | 运行时专属 API 从 `tauri` 移到运行时 crate 的扩展 trait（`WebviewCefExt`、`AppHandleWryExt` 等），同时为具体运行时与 `DynRuntime` 实现，运行时不匹配时返回 `RuntimeTypeMismatch` | alpha.0 #15985 |
| T3 | **官方 CEF 运行时** | 新增 `tauri-runtime-cef`：`send_dev_tools_message` / `on_dev_tools_protocol` / `browser_runtime_style`；Linux 侧启用 GTK4 | alpha.0 起 |
| T4 | **scheme 与版本归运行时** | `RuntimeHandle::custom_scheme_url(scheme, https)` 决定协议 URL 格式（`tauri://localhost` vs `http://tauri.localhost`），`convertFileSrc` 跟随运行时；资产路径改为从请求 URI 解析而非剥离硬编码前缀；`webview_version` 提升为 `RuntimeHandle` 必需方法 | alpha.0 #15985 |
| T5 | **Linux 现代化** | GTK3/GTK4 改为 feature 可选、可无 GTK 构建（`GtkVersionMismatch` 错误）；托盘默认 **ksni**（SNI over D-Bus），移除 libappindicator 系统依赖（`linux-libappindicator` 可回退） | alpha.0 #15787 |
| T6 | **资源解析改革** | `tauri-build` 不再把资源拷贝到 target；dev 下相对路径资源直接从源码读取（编辑不触发重编、改动即时生效）；remap 资源在每次运行首次访问时镜像到可执行文件旁；`bundle > resources` 纳入内嵌配置 | alpha.0 #15882 |
| T7 | **ACL 精简** | 自动生成的命令权限改为 manifest 上的 `commands` 列表按需物化（不再为每命令生成 allow/deny 两条）；应用命令获得隐式 `allow-*` / `deny-*` 通配权限，capability 不必逐条列举 | alpha.0 |
| T8 | **工具链与 DX** | `tauri migrate` 支持 v2→v3；移除 `macos-private-api` feature 与 `macOSPrivateApi` 配置（透明/全屏始终可用）；NSIS `install-icon`→`installer-icon`；MSRV 1.95；specta 成为可选依赖（类型化 bindings 一等公民）、tracing 可选 | alpha.0–alpha.5、CLI alpha.4 |

对 Tarui 而言，**T1–T4 与本仓库的浏览器栈分层直接同构**，是最有对齐价值的部分；T5 由 Avalonia 与 CEF 运行时主导；T6–T8 是体验与配置精简项。

## 2. 逐项差异分析

评级说明：`高` = 架构级差异，需要专项改造；`中` = 有结构差距但有清晰落点；`小` = 局部增强即可对齐；`不适用` = 技术栈差异导致无需跟进；`观察` = 由上游（Avalonia/CEF）主导，Tarui 不主动改造。

| # | Tauri v3 变更 | Tarui 现状 | 评级 | 归属 |
| --- | --- | --- | --- | --- |
| D1 | T1 运行时构建期选择（`DynRuntime` + `.runtime()` + `RuntimeNotConfigured`） | `Tarui.WebView.Abstractions` 的 `ITaruiWebView` / `ITaruiWebViewFactory` 已浏览器中立（不变量 3 守护），且 `Tarui.Shell` / `Tarui.Hosting` **只引用** `Tarui.WebView.Abstractions` + `Tarui.WebView.Avalonia`（不引用 CefGlueNext，见下方 §4.1 前提修正）；真实差距是：(a) 缺少“运行时身份”契约（name / webviewVersion / origin / resourceMode 无统一出口）；(b) `CefGlueNextWebViewFactory` 同时承担「工厂」与「CEF 运行时初始化」两个职责；(c) 组合根没有显式“运行时”概念，等价于 v2 的 `tauri::Wry` 硬编码 | **高**（已实施 V3-0，见 §4.1） | V3-0 |
| D2 | T2 运行时特有表面隔离（扩展 trait + `RuntimeTypeMismatch`） | 已实施（V3-4）：Abstractions 新增 `ITaruiRuntimeExtension` 标记与 `ITaruiWebRuntime.TryGetExtension<T>()`（默认实现报不可用），`GetRequiredExtension<T>()` 在运行时不匹配时抛 `TaruiRuntimeTypeMismatchException`（`RUNTIME_TYPE_MISMATCH`）；CEF 侧 `CefWebRuntimeExtension` 暴露 `browserRuntimeStyle` / `engineVersion` / `isInitialized` / `userAgent` / `proxyServer` / `supportsDevToolsProtocol`，值在 `Initialize` 之后惰性读取。CDP 仍走 `ITaruiDevToolsProtocol` 可选接口（`is` 探测，缺失报 `NOT_SUPPORTED`）。`UserAgent` / `ProxyServer` 仍是初始化期 CEF 配置，运行时只读——与 v3 一致（`CefSettings` 语义如此） | 中（已实施） | V3-4 |
| D3 | T3 CEF 运行时的 CDP 通道（`send_dev_tools_message` / `on_dev_tools_protocol`） | 已实施（V3-3）：`ITaruiDevToolsProtocol`（Abstractions 可选接口）+ `CefGlueNextAvaloniaWebView` 经 `CefBrowserHost.AddDevToolsMessageObserver` 收发；`plugin:webview\|send-devtools-message` 命令 + `webview://devtools-protocol` 事件（capability 门控，demo 默认不授权）；前端 `Webview.sendDevtoolsMessage` / `onDevtoolsProtocol` | 小（已实施） | V3-3 |
| D4 | T4 scheme URL 格式由运行时定义 + 资产路径从 URI 解析 | `CefGlueNextWebAppOptions` 已有可配 scheme（默认 `tarui`）+ domain（`localhost`）+ **HTTP/Scheme 双资源模式**（`TaruiAppOrigin`），语义上已达到甚至超出 v3 的 `custom_scheme_url`；差距在结构：该决策埋在 CefGlueNext 适配层，`WebViewRequestPolicy` 与策略测试仍按 `tarui://localhost` 字面假设 | 小 | V3-0 |
| D5 | T4 `webview_version` 成为运行时必需 API | `CefGlueNextPortInfo.CefVersion`（`150.0.11`）经 `CefGlueNextWebRuntime.WebviewVersion` 暴露，并通过 `plugin:webview\|runtime-info` + `@lytree/api/webview` 的 `runtimeInfo()` 抵达前端（V3-0 已落地） | 小（已实施） | V3-0 |
| D6 | T5 Linux GTK3/GTK4 可选、无 GTK 构建 | 窗口层是 Avalonia 12.1（Linux 自带 GTK3 线）；CEF 150 运行时的 GTK 链由 CEF 分发决定，Tarui 无法单独选择 | 观察 | — |
| D7 | T5 Linux 托盘默认 ksni，移除 libappindicator | `Tarui.Shell/AvaloniaTrayService.cs` 委托 Avalonia `TrayIcon`，Linux 后端由 Avalonia 决定；仓库内无 libappindicator 依赖或打包声明 | 观察 | — |
| D8 | T6 资源目录解析（dev 源路径直读、live 生效、镜像 remap） | 已实施（V3-4）：`ResourceDirectoryOptions` + `ITaruiResourceDirectory` / `ResourceDirectory` —— 打包根（显式 → `TARUI_RESOURCE_DIR` → `{exe}/resources` → exe）、开发根（`DevelopmentRoot` 存在时直读源码，改动即时生效）、remap 镜像（`MirrorDirectory` + `MirroredResources`，首次访问执行，长度与时间戳一致则跳过）、`TARUI_RESOURCE_*` 环境变量兜底；查找一律经 `IFileAccessPolicy` 校验，`resources` 只读基目录与它同根（`FileAccessPolicyOptions.ResourcesDirectory`）。与 web 资源模式（`TARUI_WEB_*`）刻意保持两套边界 | 小（已实施） | V3-4 |
| D9 | T7 ACL 精简（manifest `commands` 按需物化 + 应用命令隐式 `allow-*`/`deny-*`） | 已实施（V3-2）：`PermissionSetCatalog` + `PermissionSetDefinition` 落地「集合标识 → 具体权限」展开；`CommandRouterBuilder.BuildPermissionSets()` 从注册的 permission manifest **按命名空间自动物化** `{ns}\|default` 与 `{ns}\|other-window` / `{ns}\|other-webview`（跨目标守卫永不进入 `default`）；无插件前缀的应用命令归入 `app` 命名空间并自动获得 `app\|default`；`DefinePermissionSet` 支持插件定制覆盖、`ExcludeFromDefaultSet` 支持敏感权限豁免（CDP 通道）；集合禁止携带 scope，未知标识/展开循环/未注册成员启动期报错。demo main 清单权限条目 167 → 69 | 中（已实施） | V3-2 |
| D10 | T8 macOS 私有 API 退役 | 透明窗口走 Avalonia `TransparencyLevelHint`，Tarui 从未有 `macos-private-api` 类开关 | 不适用 | — |
| D11 | T8 specta 类型化 bindings | 已实施（V3-4，v1 → 全量 → DTO 形状）：`TaruiCommandContract` 增加 `Permission`；生成器新增 `DtoShapeReader`，在编译期把 `TaruiJsonContext.Default.{Name}` 解析成 `JsonTypeInfo<T>` 再展开 TypeScript 形状（camelCase 成员、可空成员→可选属性、`byte[]`→base64 `string`、`JsonElement`/未建模泛型→`unknown`、枚举→`number`、嵌套与自引用 DTO 递归）。`tests/Tarui.Bindings.Tests` 汇总全部 catalog 生成 `web/packages/api/generated/commands.ts`（157 个 payload 类型 + `TaruiCommandContracts` + `TaruiCommandTypes` + `TaruiCommandId` / `TaruiCommandInput` / `TaruiCommandResult`），并做幂等漂移校验；`invokeTyped` / `invokeTypedEmpty` 让前端按 command id 拿到入参与返回类型。仍不做：按插件拆分 `.d.ts` 文件、JSON Schema 导出 | 中（已实施） | V3-4 |
| D12 | T8 `tauri migrate` / NSIS 键名 / MSRV | Tarui 无历史版本迁移负担；产物为 zip / MSIX / `.app` / `.app.tar.gz`，无 NSIS；MSRV 是 Rust 侧概念 | 不适用 | — |
| D13 | 单窗口多 WebView（v2 既有模型，v3 保留 Window/Webview 独立实体） | **架构级缺口**（gap-analysis §3.1 / §5 P0-0）：`WindowOptions`→`IWebViewFactory` 为窗口↔webview 一对一强绑定。`Tarui.Plugins.Webview` 的 `IWebviewService` 契约已声明"webview 与宿主窗口解耦、可按 `webviewLabel` 独立寻址"（`navigate/get-state/devtools/set-zoom/print/eval/eval-with-callback/list` 命令 + own/other-webview 权限），即**插件层寻址已就绪，缺底层多 WebView 挂载** | **高**（已实施 V3-1） | V3-1 |
| D14 | T8 tracing 观测 | `Tarui.Plugins.Log` 已实现 renderer→MEL 双向桥接，等价 | 小/已等价 | — |
| D15 | Android 绑定宏迁移（`android_binding!` 移至 wry 运行时） | 移动端为对齐计划 §1 明确非目标；本文只对 PC 求一比一 | 不适用（PC-only） | — |

## 3. 差异汇总

- **架构级（P0）**：D1（运行时身份未显式化，已实施 V3-0）、D13（单窗口多 WebView，已实施 V3-1）。D13 曾是 v2 对齐遗留的唯一架构级缺口，v3 延续该模型，本次一并完成。
- **结构级（P1）**：D2（运行时特有表面，已实施 V3-4）、D9（权限集合与清单精简，已实施 V3-2）。
- **增强级（P2）**：D3（CDP 通道，已实施 V3-3）、D5（版本暴露，已实施 V3-0）、D8（原生资源目录，已实施 V3-4）、D11（类型化 bindings 全量，已实施 V3-4）。
- **已等价或超出**：D4（双资源模式 > `custom_scheme_url` 单一方案；请求策略的允许列表本就由 `TaruiAppOrigin` 构造）、D14。
- **上游主导**：D6、D7（Avalonia/CEF 决定 Linux GTK 与托盘后端）。
- **不适用**：D10（Avalonia 承担 macOS 私有 API 语义）、D12（无迁移负担 / NSIS / MSRV 概念）、D15（移动端，PC-only 范围外）。

> **PC 平台结论（2026-10-20）**：除 D6/D7 两个上游主导项外，Tauri v3 已公布的 PC 相关变化已全部对齐或等价覆盖，无待排期差异项。

## 4. 改造方案

### 4.1 Phase V3-0：运行时身份与扩展表面（对齐 T1/T2/T4）

**目标**：把"CefGlueNext 是唯一实现"重构为"应用选择一个 webview 运行时"，依赖方向倒转为 Shell/Hosting 只面向抽象。Tarui **不实现第二个运行时**——本阶段的价值是边界清晰化（等价 v3 把 CEF 表面隔离进 `tauri-runtime-cef`），并为未来任何替代内核留出插槽。

**实施状态（2026-10-17，V3-0 已完成）**：

- **前提修正**：规划时假设 `Tarui.Shell` / `Tarui.Hosting` 直接引用 `Tarui.WebView.CefGlueNext`，实测**不成立**——两者本就只引用 `Tarui.WebView.Abstractions`（+ Shell 的 `Tarui.WebView.Avalonia`）。故步骤 2/3/7/8 的“依赖倒转”工作量重估；`TaruiAppOrigin` 亦早已位于 Abstractions（步骤 1 的“上移”同样不成立）。本阶段据此收敛为“补运行时身份契约 + 落地 runtime-info + 用架构测试锁边界”。
- **已交付**：步骤 1（`ITaruiWebRuntime`，用 `ValueTask` 且**不含** `RunSubProcess`）、步骤 2 的运行时类型拆分（`CefGlueNextWebRuntime : ITaruiWebRuntime`）、步骤 4（`AddCefGlueWebView()` 注册运行时并解析 `TaruiAppOrigin`）、步骤 5（`plugin:webview|runtime-info` + `@lytree/api/webview` 的 `runtimeInfo()`）、步骤 8（架构测试新增 Shell/Hosting 引用 CefGlueNext 违规断言）。全部自测试与架构测试通过。
- **延迟项（本轮未做，记录原因）**：
  - 步骤 3（Hosting 调用注入的 `ITaruiWebRuntime` 编排 `Initialize/Shutdown`）：CEF 初始化绑定 Avalonia UI 线程，改为 Hosting 编排会引入提前初始化/线程时序风险；当前仍由 `CefGlueNextAvaloniaRuntime`（静态、fingerprint 幂等）+ 组合根 `finally` 承担。**待 CEF 线程模型明确后再动**。
  - 步骤 2 的 `RunSubProcess` 物理迁移：`RunSubProcess` 必须在 host builder 之前（`Program.Main`，DI 之前）调用，保留为 `CefGlueNextAvaloniaRuntime` 静态方法；`ITaruiWebRuntime` 作为 DI 实例只承担 metadata + `InitializeAsync/ShutdownAsync`。
  - 步骤 6（`ICefWebviewExtension` / CDP 扩展表面）：与 D3 同批，归入 V3-3。
  - 步骤 7（`WebViewRequestPolicy` 字面假设改为从 `TaruiAppOrigin` 构造）：该策略测试当前无 CefGlueNext 依赖，改动收益低于回归风险，择机随资源模式增强一并进行。

实施步骤：

1. `Tarui.WebView.Abstractions` 新增 `ITaruiWebRuntime`：

   ```csharp
   public interface ITaruiWebRuntime
   {
       string Name { get; }                 // "CefGlue"
       string WebviewVersion { get; }       // 对齐 RuntimeHandle::webview_version
       TaruiAppOrigin Origin { get; }       // scheme+domain 或 HTTP 基址（含资源模式）
       Task InitializeAsync(CancellationToken ct);
       Task ShutdownAsync();
       int RunSubProcess(IReadOnlyList<string> args);   // 子进程派发，非 0 表示已作为子进程运行
   }
   ```

   `TaruiAppOrigin` 从 `CefGlueNextWebAppOptions` 上移到 Abstractions（D4 的结构归位）。
2. `Tarui.WebView.CefGlueNext` 拆分：`CefGlueNextWebViewFactory` 的运行时职责（CEF `Initialize`/`Shutdown`/子进程派发、`CefSettings` 组装）移入新类型 `CefGlueNextWebRuntime : ITaruiWebRuntime`；工厂只保留"创建 webview"。`CefVersion` 常量随迁。
3. 生命周期编排（不变量 6 的 `RunSubProcess → Initialize → … → Shutdown` 链）改由 Hosting 调用注入的 `ITaruiWebRuntime`；Shell 的窗口桥接只依赖 `ITaruiWebViewFactory`。
4. 组合根：`AddCefGlueWebView()` 语义变为"注册 CefGlue 运行时实现"并保留为兼容别名（等价 v3 `.runtime(tauri_runtime_wry::Wry::default())` 的"应用选运行时"动作）；未注册运行时时启动失败并给出明确错误（对齐 `RuntimeNotConfigured`）。
5. 新增 `plugin:webview|runtime-info`（或并入应用握手快照）：返回 `{ name, webviewVersion, resourceMode, origin }`，权限 `plugin:webview|runtime-info`；`@lytree/api/webview` 增加 `runtimeInfo()`。
6. CEF 特有表面（D2）：`Tarui.WebView.CefGlueNext` 内提供 `ICefWebviewExtension`（CDP 消息收发、browser runtime style 预留位），宿主代码按需向下转型（对齐 v3 `WebviewCefExt` 扩展 trait 模式）；Abstractions 与前端 API 不感知。
7. `WebViewRequestPolicy` 及其测试中 `tarui://localhost` 字面假设改为从 `TaruiAppOrigin` 构造。
8. 架构测试更新：`Tarui.Shell` / `Tarui.Hosting` 不得引用 `Tarui.WebView.CefGlueNext` 程序集（原依赖图倒转）；CEF 特有接口仅允许出现在 CefGlueNext 程序集。

验收门禁：

- `runtime-info` 返回 CEF 版本与资源模式，且被能力闸门保护；
- Shell/Hosting 项目引用与架构测试证明仅依赖 Abstractions；
- 现有 `AddCefGlueWebView()` 用法零破坏，全部自测试与架构测试通过；
- `webview.md` §1 依赖图更新（倒转后 Shell/Hosting 不再直连 CefGlueNext）。

### 4.2 Phase V3-1：单窗口多 WebView（承接 gap-analysis §5 P0-0）

**目标**：Window 与 WebView 实体分离，一窗口可挂多个 webview。分两步交付：

**实施状态（2026-10-17，V3-1 两步已交付）**：

- **Step 1 实体分离（已交付）**：新增 `WebviewRegistry`（主索引 webview label → `WebviewEntry`，副索引 window label → webview label 列表），独立于 `WindowRegistry`；窗口条目以窗口级 fan-out sink（`WindowEventFanout`）替换单槽 `Webview`，`IWindowSinkRegistry` / `EventRouter` 签名零变更；`WebviewSession` 暴露独立 `WebviewLabel` / `WindowLabel`；`AvaloniaWebviewService` 经 `WebviewRegistry` 解析会话、`ListAsync` 枚举 webview 而非窗口；窗口关闭释放全部托管 webview。旧 1:1 路径行为不变（全部自测试通过）。
- **Step 2 挂载能力（已交付）**：`plugin:webview|create`（`parentWindow` + `bounds` + 独立 origin，宿主窗口继承 capability）、`plugin:webview|set-bounds`、`plugin:webview|destroy`；Avalonia 侧 `ShellWindowComposition.Content` 改为 `WebviewSurfaceHost`（无 bounds 填满窗口、有 bounds 按 rect 定位），`WebviewPresenter.SurfaceBounds` 承载布局；跨窗口 attach 需 `plugin:webview|create-other-window`，跨 webview 操作用 `-other-webview` 变体。前端 `@lytree/api/webview` 增加 `Webview.create` / `setBounds` / `destroy`。
- **偏差登记**：capability 仍按宿主窗口 profile 继承（webview 无独立 capability 维度，见 §6 风险）；resize 联动由 `WebviewSurfaceHost` 的布局（无 bounds 自动填满）承担，无需显式 resize 监听。

实施步骤（原文）：

1. **实体分离**（无行为变化的重构）：
   - 新增 `WebviewRegistry`（label → webview 会话）独立于窗口注册表；窗口持有 webview 集合，现有 1:1 成为"单子视图"特例；
   - `plugin:webview|*` 命令的 label 寻址落到新注册表；事件定向（`webview://*`）与 capability 的 windows 匹配规则同步按 webview label 解析。
2. **挂载能力**：
   - `plugin:webview|create`：`parentWindow` + `bounds` + 独立 `origin`，在既有窗口内创建；
   - `plugin:webview|set-bounds` / `destroy`；窗口 resize/关闭联动释放；
   - Avalonia 侧以多个 NativeControlHost 承载 windowed CEF 实例；权限沿用 own/other-webview，跨窗口 attach 需 `*-other-window`。

验收门禁：一窗口两 webview 独立导航、独立 capability 隔离、独立关闭释放；关闭 100 次动态 webview 无注册表/CEF 资源残留；1:1 旧路径行为不变。

### 4.3 Phase V3-2：权限集合与清单精简（对齐 T7）

**目标**：capability 不再逐条罗列命令，同时不放松"生产禁 `*`"红线。

**实施状态（2026-10-20，V3-2 已交付）**：

- **集合契约**：`Tarui.Ipc` 新增 `PermissionSetDefinition`（标识 + 成员 + 说明）、`IPermissionSetCatalog` / `PermissionSetCatalog`（不可变、支持嵌套展开、循环检测）、`PermissionSetNames`（命名规约）。`CommandRouterBuilder.BuildPermissionSets()` 产出「定制集合 + 自动物化集合」。
- **自动物化（对齐 v3「manifest `commands` 按需物化」）**：从已注册 permission 的 manifest 按命名空间派生 `{ns}|default`；`-other-window` / `-other-webview` 守卫单独进入 `{ns}|other-window` / `{ns}|other-webview`，**永不进入 `default`**——避免「授予整命名空间」隐式放大跨窗口/跨 WebView 能力。应用命令（无 `core:` / `plugin:` 前缀）归入 `app` 命名空间，天然获得隐式 `app|default`（对齐 v3 隐式 `allow-app-commands`）。
- **定制与豁免**：`DefinePermissionSet(...)` 覆盖同名自动集合；`ExcludeFromDefaultSet(permission)` 把必须显式授予的权限排除在 `default` 外——`plugin:webview|send-devtools-message` 已据此排除，保证 CDP 通道在「授予整个 webview 面」时仍为默认关闭。
- **加载期展开与校验**：`CapabilityLoader.Load(dir, catalog)` 展开集合；集合携带 `allow`/`deny` 直接报 `InvalidDataException`（scope 属于单条权限）；未知集合标识、展开循环报错并带文件路径；`CommandRouterComposer.Compose` 对展开后的权限做既有「未注册权限」校验，并在错误信息里列出可用集合标识。
- **组合期单次遍历**：`AddTaruiShell` 注册 `CommandRouterBuilder` 单例，`IPermissionSetCatalog` 与 `CommandRouter` 共享同一次插件遍历，杜绝「集合展开用的是另一份注册表」的漂移；未注册该单例的旧用法（测试手工组容器）回落到 `Compose` 内自建 builder，行为不变。
- **demo 改写**：`examples/demo/capabilities/main.json` 权限条目由 167 压到 69（`core:window|default` + `other-window`、`plugin:webview|default`、`plugin:sql|default`、`plugin:dialog|default`、`plugin:menu|default`、`plugin:tray|default`、`plugin:notification|default`、`plugin:updater|default` 等），带 scope 的权限保持显式；`editor.json` 仅把 42 条里的 tray / notification 两个命名空间换成集合（42 → 34），其余保持逐条，用来展示「窄权限窗口不必全用集合」。
- **偏差登记**：
  - **集合不允许携带 scope**（v3 的 scope 也绑定单条 permission，此处以硬报错替代静默广播）；由此 `plugin:fs|*`、`plugin:store|*`、`plugin:global-shortcut|*` 等 scoped 面在 demo 里仍逐条出现，集合收益集中在无 scope 的命名空间。
  - **一处已登记放宽**：`core:window|other-window` 覆盖插件注册的全部 24 个跨窗口守卫，demo 原清单只列 23 个，新增 `core:window|deny-close-other-window`（否决其他窗口的强制关闭）。该权限弱于已授予的 `core:window|close-other-window`，不构成提权，已在自测试中断言为「已知放宽」。
  - **未做 v3 的「每命令 allow/deny 双权限」形态**：Tarui 的模型是「命令 → 单个 permission id + capability 是否列举」，没有独立的 deny 清单，因此 v3 那半个动机不适用；本阶段只对齐「按需物化 + 隐式应用命令集合」两个实质点。

实施步骤（原文）：

1. 插件声明**权限集合**（如 `plugin:fs|default`、`core:window|default`）：集合在构建期展开为具体命令权限，运行时按需物化（等价 v3 manifest `commands` 列表）；
2. 应用自有命令获得隐式 `allow-app-commands` / `deny-app-commands` 集合权限（对齐 v3 隐式 `allow-*`/`deny-*`）；
3. `CapabilityLoader` 校验集合标识存在性、循环展开与未知成员；JSON Schema 与 `docs/usage/capabilities.md` 同步；
4. demo 能力清单改写为集合形式并保持语义等价（拒绝测试逐条对照）。

验收门禁：demo capability 显著缩短且全部拒绝测试结果不变；`*` 通配仍仅限隔离测试环境。

### 4.4 Phase V3-3：开发体验增强（对齐 T3/T8 的 DX 方向）

按价值排序，逐项独立可交付：

**实施状态（2026-10-18，已交付项 2/3；项 1 已随 V3-0；项 4 已于 V3-4 交付，见 §4.5）**：

- **CDP 消息通道（D3，已交付）**：Abstractions 新增可选能力接口 `ITaruiDevToolsProtocol`（`TrySendDevToolsMessage` + `DevToolsMessageReceived`），仅 `CefGlueNextAvaloniaWebView` / `CefGlueNextWebView` 实现（经 `LifeSpanHandler.OnAfterCreated` → `CefBrowserHost.AddDevToolsMessageObserver`，不改 vendored CefGlue）；`plugin:webview|send-devtools-message` 命令（权限 + `-other-webview`）与保留事件 `webview://devtools-protocol`（capability 门控，demo 默认不授权）；`CommandRouter` 新增 `NOT_SUPPORTED` 错误码；前端 `Webview.sendDevtoolsMessage` / `onDevtoolsProtocol`。
- **类型化命令契约（D11 v1，已交付）**：生成器改为从 `commands.Add("id", TaruiJsonContext.Default.Input, …)` 调用点发现命令（覆盖属性处理器与内联 lambda，5/6 参数两种签名），产出每程序集 `{Assembly}CommandCatalog`（`Contracts` = `{ Id, Input, Output }`）；新增 `tests/Tarui.Bindings.Tests` 做生成契约 ↔ 手写 `@lytree/api` 的 `invoke('…')` 字面量双向漂移校验。
- **偏差登记**：契约 v1 不含权限与 `.d.ts`（按文档延后）；生成器的「DTO 未入 JsonContext」诊断未加（分析器发布跟踪 RS2008 的维护成本高于收益，延后）；CEF 真机 CDP 收发路径无法在控制台自测，真机验收沿用 §6-10 待办；D2 的 `ICefWebviewExtension` 未单独建（CDP 已由 `ITaruiDevToolsProtocol` 覆盖，`browser_runtime_style` 等无消费者的预留位不建，避免为抽象而抽象）。

实施步骤（原文）：

1. **webview 版本暴露**（D5）：若 V3-0 未覆盖，在握手或 `runtime-info` 中落地；
2. **CDP 消息通道**（D3）：`plugin:webview|send-devtools-message` / `on-devtools-protocol` 事件（capability 门控，默认不授权），基于 `CefBrowserHost.SendDevToolsMessage` + `CefDevToolsMessageObserver`；解锁性能分析、内存快照等工具链；
3. **类型化 bindings 生成**（D11）：扩展 `Tarui.Ipc.Generators`——从命令目录 + DTO 元数据（`JsonSerializerContext`）生成命令契约（JSON 清单 / `.d.ts`），CI 校验手写 `@lytree/api` 与生成契约零漂移；全量生成模块另行评估；
4. **resourceDir 概念评估**（D8）：如引入"应用原生资源目录"，须与 `IFileAccessPolicy` 的 resources 只读 scope 统一，避免两套资源边界。

### 4.5 Phase V3-4：PC 平台一比一补齐（对齐 T2 / T6 / T8）

**目标**：把 V3-0…V3-3 之后剩余的 PC 相关差异（D2 / D8 / D11）补齐到与 v3 行为等价。每项都同时交付「契约 + 实现 + 测试 + 文档」，并维持 10 条设计不变量（无反射、无程序集扫描）。

**实施状态（2026-10-20，已交付）**：

- **D2 运行时特有表面（T2）**：`Tarui.WebView.Abstractions` 新增 `ITaruiRuntimeExtension` 标记与 `ITaruiWebRuntime.TryGetExtension<T>()`（接口默认实现报不可用，等价 v3 对 `DynRuntime` 的默认实现——新后端只需为自己真正提供的表面覆写）。`GetRequiredExtension<T>()` 在缺失时抛 `TaruiRuntimeTypeMismatchException`，`CommandRouter` 映射为稳定错误码 `RUNTIME_TYPE_MISMATCH`（对齐 v3 `RuntimeTypeMismatch`，区别于「命令不存在」`COMMAND_NOT_FOUND` 与「后端不支持」`NOT_SUPPORTED`）。CEF 侧 `CefWebRuntimeExtension` 暴露 `browserRuntimeStyle` / `engineVersion` / `isInitialized` / `userAgent` / `proxyServer` / `supportsDevToolsProtocol`，值从 `CefGlueNextAvaloniaRuntime.Options` 惰性读取，初始化前后都反映真实配置。
- **D8 原生资源目录（T6）**：`Tarui.Ipc` 新增 `ResourceDirectoryOptions` / `ITaruiResourceDirectory` / `ResourceDirectory`。解析顺序：显式 `Root` → `TARUI_RESOURCE_DIR` → `{exe}/resources`（存在时）→ `{exe}`；`DevelopmentRoot`（或 `TARUI_RESOURCE_DEV_DIR`）存在时**整体接管**，直接读源码树，编辑即时生效、不经任何拷贝；`MirrorDirectory` + `MirroredResources`（或 `TARUI_RESOURCE_MIRRORED`）在首次访问时把 remap 资源镜像到可执行文件旁，目标已最新（长度 + 时间戳）则跳过，重启不重写。所有查找只接受相对路径并经 `IFileAccessPolicy` 校验（词法包含 + 链接逃逸），`resources` 只读基目录通过 `FileAccessPolicyOptions.ResourcesDirectory` 指向同一根，杜绝「两套资源边界」。
- **D11 类型化契约全量（T8）**：`TaruiCommandContract` 增加 `Permission` 字段，生成器在 `commands.Add(...)` 调用点读取 permission 字面量；新增 `DtoShapeReader` 把 `TaruiJsonContext.Default.{Name}` 解析成 `JsonTypeInfo<T>`，再按序列化器语义展开成员（camelCase、可空→可选、嵌套 DTO 递归）。`tests/Tarui.Bindings.Tests` 汇总全部程序集 catalog，生成 `web/packages/api/generated/commands.ts`（payload 类型 + `TaruiCommandContracts` + `TaruiCommandTypes` + `TaruiCommandId` / `TaruiCommandInput` / `TaruiCommandResult`，包入口 re-export），并做**幂等**比较——文件过期即失败，`--update` 重生成。前端因此可以做 `invokeTyped('plugin:fs|read-text-file', { base: 'appData' })` 级别的编译期约束。

**验收**：`Tarui.Ipc.Tests`（资源目录解析/开发根/镜像/基目录联动）、`Tarui.WebView.Tests`（扩展命中与 `RUNTIME_TYPE_MISMATCH`）、`Tarui.Ipc.Tests`（错误码映射）、`Tarui.Bindings.Tests`（权限字段 + TS 声明漂移）全部通过；`pnpm --filter @lytree/api build|test` 通过。

**偏差登记**：

- **DTO 形状逐字段生成已补齐**（2026-10-20）：`DtoShapeReader` 在编译期读取 DTO 符号并展开 TypeScript，**形状真相仍然是 `TaruiJsonContext`**——TS 是从 DTO 符号派生的产物，不是第二份手写定义，`tests/Tarui.Bindings.Tests` 的幂等比较保证两者同步。映射刻意贴合序列化器语义：`WhenWritingNull` ⇒ 可空成员写成可选属性而不是 `| null`；`byte[]` ⇒ base64 `string`；枚举（无 `JsonStringEnumConverter`）⇒ `number`；`JsonElement` 与未建模泛型 ⇒ `unknown`（宁可 `unknown` 也不猜）。
- **仍未做「一插件一 `.d.ts`」与 JSON Schema 导出**：全部 payload 类型汇总在单个 `generated/commands.ts`（约 2100 行、157 个类型）。拆分只在出现「按插件懒加载类型」的真实需求时才引入，否则只是多一层构建耦合。
- **UA / Proxy 仅只读**：CEF 的 `CefSettings` 只在初始化期生效，运行时改写需要重启浏览器进程；v3 的 CEF 运行时同样如此，因此不提供运行时写入口（只读暴露用于诊断与前端展示）。

### 4.6 观察项（记录，不排期）

| 项 | 处置 |
| --- | --- |
| D6/D7 Linux GTK 与托盘后端 | Avalonia / CEF 主导，是 PC 范围内**仅剩**的上游主导项；若 Avalonia 托盘切 SNI 后端则自然跟进，届时更新 `docs/dev/environment.md` 依赖说明 |
| D10 macOS 私有 API | 不适用（Avalonia 承担） |
| D12 `migrate` / NSIS / MSRV | 不适用；`tarui.app.json` 键名演进遵循自有 Schema 版本化 |
| D14 tracing | MEL 已等价；OpenTelemetry 导出另立评估 |
| v3 后续 alpha/beta 增量 | 每个新版本发布后复核本文 §2 表并登记增量；RC 后冻结 V3-0/V3-2 设计细节 |

## 5. 与既有计划的关系

- `alignment-plan.md`（v2 对齐）**继续有效**：本文不替换其阶段门禁与 §15 状态表；v3 项叠加在其上，V3-1 即其 Phase 5 遗留的"webviewWindow 独立对象"缺口与 gap-analysis P0-0 的合并实施。
- 改造期间维持 10 条设计不变量：V3-0 的接口拆分不引入运行时反射；V3-2 的集合展开发生在启动期清单加载、由组合期注册表驱动（无反射、无程序集扫描）；V3-3/V3-4 的契约与 TS 生成复用既有源生成基建。
- 文档同步义务：V3-0 落地时更新 [`webview.md`](webview.md) 分层图与 [`architecture.md`](architecture.md) 模块生命周期（**已于 2026-10-17 完成**）；V3-2 落地时更新 [`ipc.md`](ipc.md) 与 `docs/usage/capabilities.md`（**已于 2026-10-20 完成**）；V3-4 落地时更新 [`webview.md`](webview.md) §3 运行时扩展、`docs/usage/configuration.md` §5 资源目录、`docs/dev/frontend-sdk.md` §2.1 生成的命令契约（**已于 2026-10-20 完成**）。

## 6. 风险

1. **alpha 漂移**：v3 尚未 RC，T1–T4 语义可能微调（如 `Runtime` trait 方法签名）。缓解：V3-0 只对齐"依赖方向与概念"，不逐字复刻 Rust API。
2. **不盲跟多运行时**：v3 的可插拔运行时对 Tarui 的意义是边界清晰与未来插槽，不是当下实现第二后端；避免为抽象而抽象。
3. **V3-1 工程量**：窗口↔webview 分离触及 Shell 生命周期与事件路由核心，须先补齐窗口生命周期回归测试再动刀（gap-analysis §5 P0-0 同结论）。
4. **权限集合的安全回归**：集合展开错误可能造成意外放行。缓解（V3-2 已落地）：`|default` 结构性排除跨目标守卫；集合禁止携带 scope；`tests/Tarui.Capabilities.Tests` 的 `DemoCapabilitiesResolveAgainstRegisteredPlugins` 用真实插件注册表展开 demo 清单，逐条断言「仍必须授予」「必须仍然拒绝」「已登记放宽」，并把 demo 清单与会话注册表做覆盖校验——集合化带来的任何语义漂移都会失败。
