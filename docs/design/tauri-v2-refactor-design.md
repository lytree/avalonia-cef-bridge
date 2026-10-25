# Tarui 对照 Tauri v2 的重构 / 重写设计

> 状态：**R0–R4-A 已实施并验证；R3-B / R4-B / R5 与条件交付项待定**  
> 基线日期：2026-10-24（R0 完成于 2026-10-24；R1/R2/R3-A/R4-A 完成于 2026-10-25）  
> 对比对象：**Tauri v2 桌面端**。版本基线沿用仓库 `gap-analysis.md`：tauri 2.12.5、CLI 2.12.4、`@tauri-apps/api` 2.12.4；官方插件按其 v2 文档与仓库基线。  
> 明确排除：Tauri v3 alpha 增量、Android/iOS、Rust 内部实现复刻。  
> 本文只给出架构与实施设计，不包含生产代码改动。

## 1. 结论

推荐采用 **“保留线上契约的渐进式模块重写”**，而不是推倒重写，也不是继续在现有 `Tarui.Shell` 上堆功能。

核心判断：

1. 当前项目已经具备 Tauri v2 桌面端的大部分能力面；推倒重写会重复实现命令路由、权限、流式 IPC、打包、更新器和大量平台边界，收益不足以抵消回归成本。
2. 当前主要问题已从“缺功能”转为“模块所有权不清、插件不完全自治、部分协议语义与文档不一致、跨平台证据不足”。
3. 重写应发生在**模块内部和模块边界**：Shell、插件、契约生成、Channel、capability 解析可以逐块替换；命令 ID、权限 ID、事件名、DTO 线格式和前端包入口原则上不变。
4. Tauri v2 是语义与工程边界参照，不是代码模板。继续使用 .NET DI / `Microsoft.Extensions.Hosting`、Avalonia 与内置 CEF；不改写为 Rust，不切换系统 WebView，不引入运行时反射或动态插件加载。

推荐路线：

```text
R0 基线与正确性修正
  → R1 Shell 瘦身、可选插件自治
  → R2 插件契约与前后端绑定自治
  → R3 Channel 背压修正；二进制 IPC 按实测决策
  → R4 WebView / remote capability 补齐
  → R5 跨平台验证与按需能力
```

R0–R2 是架构主线；R3 的背压修正是正确性主线；R3 的 raw transport、R4 的 remote URL、Isolation 和 Stronghold 均为有条件交付，不应绑架前几阶段。

---

## 2. 目标与非目标

### 2.1 目标

- 对齐 Tauri v2 桌面端的**核心进程、命令 / 事件 / Channel、插件、ACL / capability、CLI / 打包**语义。
- 让 `Tarui.Shell` 只拥有桌面壳核心，不再拥有所有可选插件的平台实现。
- 让每个 `Add*Plugin()` 注册该插件运行所需的命令、服务与生命周期，不再依赖 `AddTaruiShell()` 隐式补齐。
- 保持无反射、无程序集扫描、无动态插件加载、源生成 JSON、默认拒绝和能力闸门等红线。
- 用契约测试、外部消费者冒烟和跨平台运行证据证明重构没有改变线上行为。
- 修正已确认的协议语义缺口，避免以“与文档一致”替代“行为正确”。

### 2.2 非目标

- 不追求 Tauri 移动端 API，不复刻 Rust resource table、`Manager`、宏或 trait 形态。
- 不为了实现“第二个 WebView 运行时”而新增后端；当前 CEF 边界已经够用。
- 不重写 vendored CefGlue；上游同步与本地补丁管理是独立工作流。
- 不批量重命名命令、权限、事件或 npm 模块。
- 不把 Tauri 的 Isolation、raw IPC、remote URL 视为必须一次性照搬的前置条件。
- 不在本设计中安排 Stronghold、SQL 非 SQLite 驱动等产品按需能力的具体实现。

---

## 3. 当前代码基线与问题证据

以下数字来自当前工作区实测，而非旧文档计数：生产 C# 约 110,226 行，其中 vendored CefGlue 约 81,779 行，Tarui 自有生产代码约 **28,447 行**；23 个插件项目，28 个测试项目。`Tarui.Architecture.Tests` 已单独运行，扫描 937 个 active 文件并通过；这不代表其余测试已在本轮全部运行。

### 3.1 值得保留的资产

- `CommandRouter` 的命令注册、权限检查和稳定错误码路径清晰。
- `CommandContext` 由原生 Shell 侧确定窗口身份，不信任前端 envelope 中的 label。
- capability、scope deny 优先、原生事件接收授权和创建窗口防提权均有现有实现与测试基础。
- Window / WebView 注册表已经分离，Avalonia / CEF 边界明确。
- CLI、插件双包、源生成命令目录、TypeScript DTO 生成和 CI / Release 已具备基础。
- 控制台自测试与架构门禁适合作为渐进重写的安全网。

### 3.2 需要重构的结构问题

| # | 问题 | 当前证据 | 影响 |
| --- | --- | --- | --- |
| S1 | Shell 职责过度集中 | `Tarui.Shell` 57 个顶层 `.cs` 文件、约 7,678 行；其 csproj 直接引用 17 个插件项目 | Shell 同时成为窗口系统、插件宿主和大量平台服务实现库，修改影响面过大 |
| S2 | 可选插件不完全自治 | `AddUpdaterPlugin()`、`AddNotificationPlugin()` 等只注册 `ITaruiPlugin`；对应服务却由 `AddTaruiShell()` 注册 | 插件包不能独立表达自身组合关系，Shell 必须知道每个可选插件 |
| S3 | 官方 DTO 与第三方契约模型不统一 | 官方插件大量使用中央 `TaruiJsonContext`；第三方脚手架强调插件自有 context，但只生成 DTO 占位和注释掉的命令 | 官方插件仍要求改核心契约；第三方示例不是可直接运行的完整插件 |
| S4 | Channel 对插件 DTO 的支持不完整 | `ChannelContext.Bind<T>()` 固定从中央 `TaruiJsonContext.Default.GetTypeInfo(typeof(T))` 取元数据 | 第三方插件无法自然使用自己的流式 DTO，违背插件契约自治 |
| S5 | 前端仍是“生成类型 + 大量手写调用面” | `generated/commands.ts` 约 2,147 行，手写 `ipc.ts` / `window.ts` / `fs.ts` 等仍负责命令 ID 与参数组装 | 类型漂移已降低，但命令调用与模块 facade 仍有双维护成本 |
| S6 | 文档与代码基线存在漂移 | 测试数量、Hosting / Shell 描述、插件契约示例、Channel 背压说明等不完全一致 | 后续重构若只信文档，容易固化错误假设 |

S1/S2 是本次架构重构的主因；S3–S5 是插件 SDK 主线；S6 要求每阶段以代码与运行证据更新文档。

### 3.3 需要先修正的正确性与门禁问题

#### C1：架构规则未覆盖文档明确禁止的 API

`TaruiShellServiceCollectionExtensions.CreateExtensionRegistration<T>()` 在无 factory 时调用 `ActivatorUtilities.GetServiceOrCreateInstance<T>()`。文档红线明确禁止 `ActivatorUtilities`，但架构规则只搜索完整单词 `Activator`，因此当前门禁仍会通过。

设计要求：移除该隐式构造路径；无 factory 时通过显式 `AddTransient<T>()` + `GetRequiredService<T>()` 解析，或要求调用方提供 factory。架构门禁新增 `ActivatorUtilities` 字面规则及正反向测试。

#### C2：多 WebView 定向事件路由使用了错误注册表

`EventRouter` 只持有 `IWindowSinkRegistry`。`EmitToWebviewAsync(webviewLabel, ...)` 当前直接委托 `EmitToWindowAsync(webviewLabel, ...)`，因此非 1:1 场景下会按窗口 label 查找 WebView label。`WebviewRegistry` 虽然已有独立 sink / capability 查询，却没有被该路径使用。

设计要求：窗口定向走 `IWindowSinkRegistry`，WebView 定向走 `IWebviewSinkRegistry`；补充同一窗口两个不同 WebView label 的定向送达、未授权拒收和销毁后不残留测试。

#### C3：Channel 的“天然背压”假设不成立

`WebviewSession.SendAsync()` 最终调用 `ExecuteScriptAsync()`；CEF 适配层明确说明该调用是 fire-and-forget，只等待 UI 线程调度，不等待 JS `Channel.onmessage` 完成。前端 `Channel.push()` 也同步调用回调，没有消费回执。`TaruiChannel<T>` 的无 sink 分支同样使用 unbounded channel。

因此，当前 await 链可以约束原生脚本提交速度，却不能证明 renderer 消费侧有界。必须先设计显式信用额度 / ACK 背压，再讨论 raw/binary 性能优化。

---

## 4. 与 Tauri v2 的架构对比

| 维度 | Tauri v2 | Tarui 当前状态 | 设计决策 |
| --- | --- | --- | --- |
| 进程模型 | Core process 管理窗口和全部特权 IPC；系统 WebView 负责渲染 | Hosting / Shell 对应 Core；CEF renderer 对应 WebView process | 保留现有模型；CEF 内置是包体与一致性取舍，不改系统 WebView |
| Command | `invoke`，JSON 为主，也支持 raw request / response | 无反射命令路由、源生成 DTO 已具备；仅 JSON envelope | 保留命令模型；raw 作为显式新增传输能力，不改现有 DTO 的 base64 语义 |
| Event | 单向、定向 / 广播；核心事件权限控制 API | 定向 / 广播、原生前缀保护、接收 capability 均已具备 | 保留更严格的接收授权；先修正多 WebView 定向路由 |
| Channel | 有序流；支持 JSON 与 raw payload | fs / HTTP / Shell / WebSocket 已使用，但消费侧背压不完整 | 先实现可验证 ACK 背压；raw channel 后续按基准测试决定 |
| Capability | windows / webviews 目标、platforms、remote URLs，多 capability 合并 | windows / platforms / permissions / events 已有；WebView 继承窗口；无 remote | 增加 `webviews` 与可选 `remote`；来源必须由原生 CEF 提供，不信任 JS |
| 默认授权 | 插件命令默认拒绝；应用命令存在较宽默认语义 | 全部命令显式授权，缺省拒绝 | 保留 Tarui 更严格语义，不为“相同”而放宽 |
| Scope | permission 可关联 allow / deny scope，插件读取并执行 | PathScope、URL scope、deny 优先、persisted overlay 已有 | 保留；scope 继续只绑定单条权限，不挂到集合 |
| 插件形态 | crate + 可选 guest-js；编译期链接；命令、权限、状态和生命周期可归插件所有 | NuGet + guest-js 已有；命令在插件，部分服务与生命周期集中在 Shell | 学习“自治所有权”，不复制 Rust API；优先复用 DI 与 `IHostedService` |
| 插件生命周期 | setup、navigation、webview ready、event、drop hooks | DI、`IHostedService`、window extension、WebView policy 可覆盖多数场景 | 暂不新建大而全 hook 接口；出现两个以上真实需求后再抽象 |
| 多窗口 / 多 WebView | Window 与 WebView 为独立实体 | 实体已分离；事件与 capability 仍有 1:1 遗留假设 | 完成 label、事件和授权三个维度的真正分离 |
| Isolation | 可选 sandbox iframe + 每运行期密钥 + AES-GCM 拦截 | 无等价机制；已有 CSP、导航策略、capability | 单列安全 ADR；不以普通 JS hook 冒充等价隔离 |
| 状态与托管 | `AppHandle` / `Manager` / managed state | .NET DI、配置、日志、`IHostedService` | 保留 .NET 惯用模型，不引入第二套状态容器 |
| 前端绑定 | 核心 API + 插件 guest-js；类型化 bindings 工具链 | DTO 已生成，facade 主要手写 | 生成低层调用与类型，保留少量有人体工学价值的手写 facade |
| CLI / bundler | init / dev / build、安装器、updater 产物 | init / dev / build、zip / MSIX / `.app`、签名 `latest.json` 已有 | 持续增强，不重写 CLI |
| 平台覆盖 | 桌面与移动；桌面三平台 | 多数能力 Windows 已验证，macOS / Linux 证据不均 | PC-only；将“未真机验证”与“已实现”分开报告 |
| 功能余量 | Stronghold、更多 SQL 驱动、updater 附加 API 等 | 仍有少量缺口 | 属产品 backlog，不构成推倒重写理由 |

结论：**Tauri v2 的功能追赶已不再是主线，架构所有权、协议正确性和验证体系才是主线。**

---

## 5. 路线选择

| 路线 | 做法 | 优点 | 主要代价 / 风险 | 结论 |
| --- | --- | --- | --- | --- |
| A. 全量重写 | 新建解决方案，重新设计契约、Shell、插件、IPC、CLI | 无历史包依赖，可自由选择全部 API | 丢失现有安全与平台实现证据；长时间双维护；最终仍要面对同一批 CEF / OS 难题 | **不采用** |
| B. 局部修补 | 保留 Shell 结构，只修 bug、补 API | 单步改动小 | 插件继续依附 Shell；中央 DTO 与手写 facade 成本持续累积 | **不足以解决问题** |
| C. 渐进式模块重写 | 冻结线上契约，逐模块替换实现与依赖方向，每阶段独立验收 | 可复用测试与原生实现；每步可回退；能真正解决所有权问题 | 需要严格兼容纪律和阶段门禁 | **推荐** |

如果未来要切换渲染引擎或支持移动端，应另立技术评估，不应把本路线悄悄扩大成全量重写。

---

## 6. 目标架构

### 6.1 项目与依赖方向

```text
应用组合根
  ├─ Tarui.Hosting
  ├─ Tarui.WebView.CefGlueNext
  └─ 显式选择的 Tarui.Plugins.*

Tarui.Hosting
  → Tarui.Shell
      → Tarui.Ipc → Tarui.Contracts
      → Tarui.WebView.Abstractions / Tarui.WebView.Avalonia
      → 仅核心 Window / Webview / Event / System 契约

Tarui.WebView.CefGlueNext
  → Tarui.WebView.Abstractions / Tarui.WebView.Avalonia
  → vendored CefGlue

可选插件
  → Tarui.Ipc / Tarui.Contracts
  → 仅在确需 Avalonia 窗口集成时引用 Tarui.Shell
  ↛ 不得被 Tarui.Shell 反向引用
```

核心约束：

- `Tarui.Shell` 不再引用 Updater、DeepLink、Notification、Autostart、GlobalShortcut、Dialog、Menu、Tray、WindowState、Log 等可选插件。
- 可选插件需要窗口注册表或 Avalonia 服务时，可以依赖 Shell；依赖方向必须单向，由架构门禁检查。
- `Tarui.Contracts` 逐步收敛为 IPC envelope、共享原语和核心 DTO；不引用 Avalonia、Hosting 或插件。
- CefGlue 入口规则、生命周期顺序、源生成 JSON 与显式注册全部保持不变。

### 6.2 Shell 最终只保留

- Window / WebView 实体、registry、attach / present / surface 生命周期。
- 原生窗口、窗口效果、显示器和核心窗口服务。
- capability 解析、命令组合、`IpcDispatcher` 接入和事件路由。
- WebView 导航 / 下载策略、拖放与核心 WebView 会话。
- 主窗口启动、窗口扩展和核心系统集成。

### 6.3 插件标准解剖

```text
Tarui.Plugins.Foo/
  Contracts.cs                 # 新插件默认拥有 DTO
  FooJsonContext.cs            # 插件自己的源生成元数据
  FooPlugin.cs                 # 命令与权限集合
  FooCommands.cs               # 参数校验和调用适配
  FooService.cs                # 平台中立实现
  FooService.Windows.cs        # 按需平台实现
  FooServiceCollectionExtensions.cs
  permissions/                 # 发布用权限描述
  guest-js/                    # 生成类型 + 薄 facade
```

`AddFooPlugin()` 必须注册该插件命令所需的全部服务；需要后台生命周期的插件使用显式 `IHostedService`，需要窗口扩展时使用现有显式 registrar。不得要求 `AddTaruiShell()` 预先“偷偷”注册插件服务。

---

## 7. 分阶段设计

### R0：冻结基线并修正正确性缺口

**目标**：在大规模移动代码前，建立可信的契约与测试基线。

工作项：

1. 修正 C1：移除 `ActivatorUtilities` 路径，补齐架构规则与门禁自测试。
2. 修正 C2：`EventRouter` 分别注入 window / webview registry，独立处理定向和 capability 检查。
3. 建立契约快照：命令 ID、权限 ID、事件名、DTO JSON round-trip、错误码、demo capability 展开结果和 npm 导出。
4. 为 C3 增加“慢消费者”表征测试；在显式背压落地前，该测试应证明当前语义，不再以文档宣称有界。
5. 运行全部现有测试、前端 vitest / lint / build 和架构门禁，记录真实基线；同步修正文档数量与分层描述。

退出条件：

- 多 WebView 定向事件测试通过；`ActivatorUtilities` 被源码与门禁双重排除。
- 契约快照可用于后续 PR 的自动对比。
- 基线结果真实记录，不沿用“文档称全部通过”。

#### R0 实施记录（2026-10-24）

已完成：

1. **C1**：`CreateExtensionRegistration` 不再走 `ActivatorUtilities`。无 factory 时由 `AddWindowExtension<T>()` 显式 `TryAddTransient<T>()`，注册器声明的扩展由调用方显式注册；`WindowExtensionBuilder.RequiredServices` + `EnsureRequiredServicesAreRegistered` 在组合期用 `IServiceProviderIsService` 做存在性校验并给出可执行错误。架构门禁新增 `ActivatorUtilities` 字面规则，并加入正反向自测试（`RuleSelfTests`，6 条样例：隐式 DI 构造、`Activator`、`MethodInfo` 命中；行注释、字符串字面量、无关标识符不命中）。
2. **C2**：`EventRouter` 改为显式注入 `IWindowSinkRegistry` + `IWebviewSinkRegistry`；`EmitToWebviewAsync` 走 webview 注册表并按该 surface 的 capability 复查。新增 `RouterAddressesWebviewsIndependentlyOfWindows` 覆盖同窗口多 surface 定向、兄弟 surface 不串扰、窗口 sink 不代收、保留事件按 surface 授权、未知 label 静默丢弃。
3. **C3**：新增 `ChannelSendHasNoCreditBackpressureAsync` 表征测试，固定 5,000 帧全部滞留在途的现状，并注明 R3-A 落地后必须替换为有界断言。
4. **契约快照**：`tests/Tarui.Capabilities.Tests/contracts/capability-surface.json` 冻结保留事件前缀、插件注册权限、demo 两份清单的展开结果，支持 `--update` 重新生成；命令 ID 与 DTO 形状仍由 `Tarui.Bindings.Tests` 生成 `commands.ts` 门禁覆盖。

实施中发现并修正的基线问题（原先并非绿灯）：

- `tests/Tarui.Shell.Tests/Program.cs` 存在多余 `}`，该测试项目**此前无法编译**。
- `web/packages/api/generated/commands.ts` 与实际命令目录漂移（Positioner 移除、`CloseWindowOptions.force`、`WindowOptions.parent/modal` 等），已用 `--update` 重新生成。
- `Tarui.Capabilities.Tests` 断言 `core:window|deny-close-other-window`，而窗口插件早已不再注册该权限（由 `close-other-window` 取代），断言改为“集合展开与插件注册一致且不臆造权限”，并同步修正 `tauri-v3-alignment.md` 的偏差登记。
- `docs/design/ipc.md`、`docs/usage/troubleshooting.md`、`docs/dev/debugging.md`、`docs/usage/frontend-bridge.md`、`docs/design/alignment-plan.md` 中“await 天然背压”的表述已改为真实语义。

验证结果：`dotnet build tarui.net.slnx` 0 警告 0 错误；28 个 `tests/Tarui.*.Tests` 全部通过；`pnpm lint` 0 错误（1 条既有 `react(purity)` 警告）、`pnpm build` 通过。

未纳入 R0、留待后续阶段：

- 错误码快照：`CommandRouter` 的错误码目前是字面量（`COMMAND_NOT_FOUND`、`PERMISSION_DENIED` 等），先要引入集中常量才能快照，归入 R2 与插件契约一起做。
- npm 导出面快照：由 `packages/api/index.ts` 手工维护，计划在 R2 生成低层调用时一并生成导出清单。

### R1：Shell 瘦身与可选插件自治

**目标**：解决 S1/S2，保持命令和前端行为不变。

建议按依赖复杂度分两组迁移：

| 组别 | 插件 | 迁移内容 |
| --- | --- | --- |
| UI 中立优先组 | Updater、Autostart、GlobalShortcut、Notification、DeepLink、Log、WindowState | 服务、配置、平台实现、事件桥和 hosted service 移入插件；事件发送尽量依赖 `IEventSender`，不依赖具体 `EventRouter` |
| Avalonia 集成组 | Dialog、Menu、Tray | Avalonia 实现移入对应插件；插件可引用 Shell，Shell 删除反向引用 |

迁移规则：

- `AddTaruiShell()` 删除上述插件的服务注册；`Add*Plugin()` 同时注册插件与服务。
- 组合根继续显式调用同样的 `Add*Plugin()`，Demo / 模板 / 测试同步调整依赖。
- 平台服务保留现有类型命名空间与行为，先移动所有权，不顺手重写算法。
- 需要避免重复注册时使用明确的 `TryAdd*` 语义；不得靠注册顺序覆盖来修复冲突。
- Toast 仍作为 Notification 的可选升级载体，不因迁移改变通知行为。
- Windows 窗口效果、badge、窗口 registry 和 WebView 生命周期继续属于 Shell。

退出条件：

- Shell csproj 不再引用可选插件；架构测试禁止回退。
- 最小应用只注册 Shell + CEF + 核心插件即可运行，不携带 Updater / Notification 等实现。
- 每个可选插件独立注册后命令可用；未注册插件时得到 `COMMAND_NOT_FOUND`，而不是服务解析异常。
- 所有契约快照不变，相关插件测试与外部 NuGet 消费者冒烟通过。

#### R1 实施记录（2026-10-25）

- 10 个可选插件（Updater、Autostart、GlobalShortcut、Notification、DeepLink、Log、Dialog、Menu、Tray、WindowState）的平台实现已从 `Tarui.Shell` 移入各自插件包，含 `WindowsFileAssociationRegistrar`、`DeepLinkUri` 等遗留遗漏文件。
- `Add*Plugin()` 现在注册完整服务（含平台后端与 hosted service），`AddTaruiShell()` 不再注册任何可选插件服务；`Shell csproj` 对 10 个可选插件项目的引用全部移除，架构门禁新增依赖方向规则防止回退。
- 事件发送统一走 `IEventSender`（Menu/Tray/GlobalShortcut/Log/Notification/Updater/DeepLink），插件不再依赖具体 `EventRouter`。
- `Shell.Tests` 新增 `OptionalPluginsOwnTheirServices`：未注册插件时命令目录不含其命令、服务不可解析；注册后全部可用。

### R2：插件契约与前后端绑定自治

**目标**：官方与第三方插件使用同一种可发布模型，消除中央契约和手写调用的双维护。

工作项：

1. 新插件默认使用自己的 DTO + `JsonSerializerContext`；核心只保留共享原语。
2. 增加 `ChannelContext.Bind<T>(string? id, JsonTypeInfo<T> typeInfo)` 之类的显式元数据入口；现有中央 context 重载在兼容期保留。
3. 官方插件 DTO 迁移单独提交，保持命名空间和线格式；C# 二进制兼容若无法保留，进入明确的版本窗口和迁移说明，不伪装成无破坏。
4. 生成器输出命令、权限、DTO 形状与插件归属；生成低层 TS 调用和类型，手写 facade 只保留窗口对象、Channel、Store 等人体工学封装。
5. `tarui plugin init` 生成可运行的 ping 命令、插件 JSON context、权限、TS 调用和自测试，而非注释掉的示例。
6. `tarui plugin pack` / CI 增加外部应用消费测试：显式引用 NuGet + npm、注册插件、授权、调用成功、未授权拒绝。
7. 绑定导出继续走编译期生成 catalog / exporter，不加载程序集、不扫描类型。

退出条件：

- 第三方插件无需修改 `Tarui.Contracts` 即可增加命令、DTO 和 Channel 类型。
- 生成物幂等；命令 ID、权限或 DTO 漂移会使 `bindings check` 失败。
- 官方 npm 模块路径与公开调用方式保持兼容。

#### R2 实施记录（2026-10-25，部分完成）

- `ChannelContext.Bind<T>(string? id, JsonTypeInfo<T> typeInfo)` 已增加；中央 context 重载保留。
- 错误码集中为 `TaruiErrorCodes`（`COMMAND_NOT_FOUND` / `PERMISSION_DENIED` / `INVALID_MESSAGE` / `BRIDGE_ERROR`），全部字面量替换完毕；契约快照新增 `errorCodes` 数组并有常量冻结断言。
- `tarui plugin init` 生成可运行骨架：真实注册的 ping 命令 + 插件自有 `JsonContext` + `EmptyArgs`/`PingResult` DTO、真实 `invoke` 的 guest-js、含「ping 往返 + 未授权拒绝」两个自测试家族的 `tests/`。已用 `--local` 端到端验证（构建 + 自测试通过）。顺带修复脚手架 csproj 模板中 `Tarui.Ipc.Generators` 引用跨行断裂与依赖版本降级两处既有缺陷。
- 未完成（留待后续）：官方插件 DTO 批量迁移自有 context（第 3 项）、生成器输出低层 TS 调用与插件归属（第 4 项）、`plugin pack` 外部消费测试（第 6 项）。

### R3：Channel 正确性优先，raw transport 按实测决策

#### R3-A：显式背压（必须）

设计一种可协商的 `channel-credit-v1` 控制协议：

1. 原生 channel 建立时获得有限 credit，例如 16 帧；每次 `SendAsync` 先获取一个 credit，再序列化和投递。
2. 前端完成 `onmessage` 调用后发送 ACK；若回调返回 Promise，则 await 后再 ACK。无 handler 时维持当前丢弃语义并立即 ACK。
3. credit 用尽后生产者等待；ACK 释放 credit。webview 销毁、channel 取消或命令取消必须唤醒等待者并终止流。
4. ACK 是 bridge 控制消息，不是应用命令，不要求应用额外授予权限；只能释放该 session 已创建且仍活跃的 channel credit。
5. label 和 channel 所有权以原生 session 为准；拒绝其他 WebView 的伪造 ACK、重复 ACK、超上限 ACK 和已销毁 channel ACK。
6. 新旧前后端通过握手特性协商；老前端不声明 credit 能力时才走 legacy 模式，避免新原生无限等待旧前端 ACK。

验收：慢消费者时原生在途帧数有界；消费者恢复后继续有序传输；关闭 / 取消无死锁；伪造 ACK 不影响其他 channel；fs / HTTP / Shell / WebSocket 回归通过。

#### R3-A 实施记录（2026-10-25）

- `channel-credit-v1` 已实现：前端 `Channel` 构造时发送握手控制帧；`WebviewSession` 按 channel 维护 `ChannelCreditWindow`（默认 16），`SendAsync` 先取信用再投递；前端 `onmessage`（支持异步）落定后回发 ACK 归还信用。
- 协议细节与设计一致：控制帧不进路由、无需授权；伪造/重复/未知 ACK 忽略；`Complete(channelId)` 与会话销毁关闭窗口并唤醒挂起生产者（`ChannelClosedException`）；发送异常归还信用；未握手的老前端走 legacy 无界路径。
- `ChannelCreditWindow` 采用显式等待队列实现（SemaphoreSlim 方案在 Close 时与唤醒等待者归还槽位存在双重释放竞争，已弃用）。
- R0 表征测试替换为有界断言：`ChannelCreditBoundsInFlightFramesAsync`（信用耗尽生产者必须等待、单个 ACK 恰好释放一帧）、重复 ACK 与关闭唤醒的单测；`Shell.Tests` 增加会话级握手/ACK/伪造 ACK/Complete 唤醒测试。
- 前端 `ipc.ts`：握手一次、ACK 在 handler 落定后发送；无桥时静默降级。
- 已知语义偏差：本实现未强制「每个 window 事件都带原生背压」，窗口事件路径维持 fire-and-forget（事件是低频通知，不构成堆积风险）。

#### R3-B：raw / binary IPC（条件交付）

先做独立 spike 和基准，不直接替换现有桥：

- 保留 JSON envelope 与现有 `byte[]` base64 DTO 行为；新增显式 raw 命令注册与 `ArrayBuffer` / `Uint8Array` API。
- 评估 CEF custom scheme / resource handler 的请求响应通道，避免大 payload 经 base64 + JS 字符串往返。
- Channel raw 帧必须带原生关联的 channel ID、长度和顺序信息；仍受 R3-A credit 与 capability 约束。
- 不以性能为由绕过 `CommandRouter`；命令身份、调用上下文和权限检查保持同一实现。
- 若 CEF 方案无法保持来源可信、取消语义或生命周期，保留 JSON 通道并记录有证据的“不跟进”结论。

验收以真实基准为准：大二进制不再 base64 膨胀、内存有界、吞吐不劣于现状、普通小 JSON 命令没有明显回退。

### R4：capability 的 WebView 与 remote 维度

#### R4-A：独立 WebView 选择器（推荐主线）

在现有 capability JSON 上增量支持 `webviews`：

- 旧清单只有 `windows` 时行为不变。
- `webviews` 按 WebView label 精确匹配；同一窗口不同 WebView 可获得不同权限与事件集合。
- 同一 capability 同时声明 `windows` 与 `webviews` 时必须同时匹配，避免目标放大；多个 capability 仍按合并语义处理，deny scope 优先。
- 创建 WebView 时执行防提权检查，不能借新 label 或宿主关系获得超过创建者 / 宿主的权限。
- 窗口 fan-out 在投递到每个 WebView 前再按该 WebView capability 检查原生事件，不再只检查窗口级集合。

#### R4-A 实施记录（2026-10-25）

- capability JSON 增量支持 `webviews`（精确 label 匹配）；`windows` 缺省时允许 webview-only 清单，两者都缺省则启动失败。
- `CapabilityLoader.LoadManifests` 返回 `CapabilityManifests(Windows, Webviews)` 两个键空间；`ICapabilityProvider` 增加 `WebviewCapabilities`（默认实现为空映射，旧提供者不受影响）。
- WebView surface 的实际授权 = 宿主窗口 capability ∪ 命中 label 的 `webviews` 授权（`CapabilitySet.Merge`，scope 的 allow/deny 各自并集，deny 不会因合并消失）。`WebviewAttacher` 两条挂载路径（Attach / AttachWebview）都按此解析。
- 语义偏差说明：设计原文写「同一 capability 同时声明 windows 与 webviews 时必须同时匹配（AND）」；实现采用的是**加法语义**——`windows` 授权作用于窗口配置，`webviews` 授权作为额外 surface 授权叠加。因为合并只增不减，不会产生权限放大；AND 交集语义留待有真实需求时再引入。
- 窗口 fan-out 在投递到每个 surface 前按该 surface 的 capability 复查事件（纵深防御；当前合并模型下不会缩小窗口授权，`Shell.Tests` 有正反两个用例）。
- 创建 WebView 防提权沿用既有 `ResolveForCreate` 比较（目标集不得超出调用者集），webviews 授权来自静态清单而非调用者，不构成新的提权面。

#### R4-B：remote URL（仅确有远程内容需求时）

- 增加 `remote.urls`，默认不存在即表示远程来源无 IPC 权限。
- `CommandContext` 增加原生提供的来源信息；来源从 CEF frame / browser 状态获取，绝不读取前端自报 origin。
- 顶层导航后重新解析 capability；无法匹配时拒绝全部特权 IPC，并撤销旧来源的 channel / pending 授权状态。
- 默认只允许主 frame 调用；iframe 支持另立威胁模型与验收。
- 远程页面权限不得隐式授予本地文件、Shell、Updater、CDP 等高风险能力。

此阶段需要同步 JSON Schema、启动校验、demo 反例和 `docs/usage/capabilities.md`。

### R5：验证矩阵与按需 backlog

- Windows / macOS / Linux 至少运行无 UI 的 IPC、capability、策略、HTTP、SQL、插件契约测试。
- macOS / Linux 的 DeepLink、notification、global-shortcut 与 updater 分别记录“代码实现 / CI 非 UI 验证 / 真机验证”。
- 真实 windowed CEF 验收多窗口、多 WebView、反复创建销毁、文件拖放、导航、下载和 renderer 崩溃。
- Stronghold、SQL 非 SQLite 驱动、updater restore / verify、Isolation 分别按产品需求立项，不与架构重构捆绑。

---

## 8. 兼容策略

### 必须冻结的线上契约

- 命令 ID、权限 ID、事件 ID。
- DTO 字段名、可空性、数值 / 枚举 / `byte[]` 线格式。
- 错误码与授权失败语义。
- `@lytree/api` 的公开模块路径和主要调用方式。
- capability 旧文件在新增字段缺省时的行为。

### 可调整但需明确发布的实现面

- 类型所在程序集、插件对 Shell 的依赖方向、内部服务注册位置。
- Shell 内部类型拆分和插件平台实现文件布局。
- C# 中央 `TaruiJsonContext` 的迁移；如无法保留源码 / 二进制兼容，必须进入版本迁移说明。
- 新 raw 命令、credit 控制协议和 capability 新字段均增量添加，不改写旧命令含义。

---

## 9. 测试与架构门禁

### 9.1 每阶段公共门禁

```powershell
dotnet restore tarui.net.slnx --configfile NuGet.Config
dotnet build tarui.net.slnx --no-restore
# 运行全部 tests/Tarui.*.Tests，而非只运行架构测试
dotnet run --project tests/Tarui.Architecture.Tests --no-build

# web/
pnpm lint
pnpm build
# @lytree/api vitest 与 bindings 幂等检查
```

### 9.2 新增结构门禁

- `Tarui.Shell` 不得引用可选插件项目。
- `ActivatorUtilities`、反射、程序集扫描和动态加载全部被明确扫描。
- 插件除批准的 Avalonia 集成方向外不得引用 Shell / Hosting；任何插件不得直接引用 CefGlue。
- `Tarui.Contracts` 不得引用 UI、Hosting 或插件实现。
- 每个 `Add*Plugin()` 都有“仅注册该插件即可解析其命令依赖”的组合测试。

### 9.3 新增行为门禁

- 多 WebView：独立 label、定向事件、独立 capability、销毁后无残留。
- Channel：慢消费者、有界在途、ACK 所有权、取消、关闭、重复 ACK。
- 插件：中央 Contracts 零修改的 out-of-tree 命令 + DTO + Channel 冒烟。
- Capability：webviews 匹配、remote 默认拒绝、导航后来源变更、防提权。
- 契约：命令 / 权限 / 事件 / DTO / npm 导出快照无未登记差异。

---

## 10. 建议 PR 切分

1. `refactor: tighten architecture baseline and route webview events correctly`（R0）
2. `refactor: move platform-neutral optional services into plugins`（R1 第一组）
3. `refactor: move Avalonia plugin services out of shell`（R1 第二组）
4. `feat: make plugin contracts and bindings self-contained`（R2）
5. `fix: add negotiated flow control to channels`（R3-A）
6. `feat: resolve capabilities per webview`（R4-A）
7. `feat: evaluate raw ipc transport`（先 spike / ADR，再决定实施）
8. `feat: authorize remote webview origins`（仅有真实需求时）

每个 PR 都应能独立构建、独立回退；不得把“移动文件”和“改变协议语义”混在同一提交。

---

## 11. 风险与控制

| 风险 | 控制措施 |
| --- | --- |
| DI 注册移动后出现缺服务或重复覆盖 | 插件级组合测试、`TryAdd*` 明确语义、最小应用与完整 Demo 两类冒烟 |
| 插件引用 Shell 后形成循环 | Shell 删除可选插件引用；架构测试固定依赖方向 |
| 移动 DTO 导致 JSON 或 C# API 漂移 | JSON round-trip 快照、命名空间保持、独立版本迁移说明 |
| UI 线程亲和性在移动服务时被破坏 | 先移动不重写算法；保留现有 Dispatcher / Avalonia 调用路径并跑窗口测试 |
| credit 协议导致旧前端死锁 | 握手协商、legacy 回退、取消 / 销毁唤醒和超时诊断 |
| raw transport 绕过权限或信任前端 label | 复用同一路由和原生 CommandContext；安全审查先于性能收益 |
| remote capability 扩大攻击面 | 默认拒绝、原生 origin、主 frame 限定、敏感权限显式排除 |
| 文档再次落后于代码 | 每阶段同步 design / dev / usage；能力矩阵和绑定尽量由代码生成 |

---

## 12. 评审时需要确认的事项

本设计给出的默认选择如下；若评审无异议即可按 R0 开始：

1. 采用渐进式模块重写，不新建替代解决方案。
2. 保持 Avalonia + 内置 CEF，不评估切换系统 WebView。
3. R1 允许调整程序集依赖和 C# 内部组合关系，但保持 IPC / TS 契约。
4. R3-A 与 R4-A 进入主线；raw IPC、remote URL、Isolation、Stronghold 不阻塞主线。
5. 先完成当前工作区改动的基线确认，再按上述 PR 顺序实施，避免覆盖尚未提交的工作。

---

## 13. 参考资料

### Tauri v2 官方

- Process Model：<https://v2.tauri.app/concept/process-model/>
- IPC：<https://v2.tauri.app/concept/inter-process-communication/>
- Isolation Pattern：<https://v2.tauri.app/concept/inter-process-communication/isolation/>
- Capabilities：<https://v2.tauri.app/security/capabilities/>
- Plugin Development：<https://v2.tauri.app/develop/plugins/>
- JavaScript core / raw payload：<https://v2.tauri.app/reference/javascript/api/namespacecore/>

### 本仓库

- `docs/design/architecture.md`：当前所有权与生命周期。
- `docs/design/gap-analysis.md`：Tauri v2 功能面基线；本文补充结构与代码实测差异。
- `docs/design/alignment-plan.md`：能力实施历史与状态。
- `docs/design/ipc.md`、`plugin-system.md`、`hosting.md`：现有契约与组合方式。
- `src/desktop/Tarui.Shell/TaruiShellServiceCollectionExtensions.cs`：当前集中注册与隐式 DI 构造路径。
- `src/desktop/Tarui.Shell/EventRouter.cs`、`WindowRegistry.cs`、`WebviewRegistry.cs`：定向事件证据。
- `src/desktop/Tarui.Shell/WebviewSession.cs`、`src/webview/Tarui.WebView.CefGlueNext/CefGlueNextAvaloniaWebView.cs`：Channel 投递与 fire-and-forget 证据。
- `src/core/Tarui.Contracts/TaruiChannel.cs`：中央 JSON metadata 与 unbounded channel 证据。
