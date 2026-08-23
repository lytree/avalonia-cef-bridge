# Tarui 开发模式与分发体系设计（对齐 Tauri 工作流）

> 状态：设计稿（已实施 W0 打包基线 / W1 前端 SDK 构建化 / W2 CLI MVP / W3 应用模板与 init，状态见 §11 各行标注）
> 基线：2026-08-22，仓库处于 `tauri-desktop-alignment-plan.md` Phase 6 之后（Deep Link 已交付，Windows 已验证）
> 关联文档：`docs/tauri-desktop-alignment-plan.md`（能力对齐主线）、`docs/architecture.md`、`docs/hosting.md`

## 1. 目的与范围

**目的**：能力对齐主线解决了"桌面应用能做什么"，本文档补齐"工程化分发"维度——让 tarui.net 具备与 Tauri 一致的完整开发模式：

1. **应用开发者视角**：一条命令脚手架（`tarui init`）、一条命令开发（`tarui dev`）、一条命令构建发布（`tarui build`）。
2. **SDK 视角**：后端 NuGet 包族与前端 `@lytree/api` 的构建、版本化、发布与兼容性承诺。
3. **插件作者视角**：插件的脚手架、开发调试、双包（NuGet + npm）发布与第三方应用接入。

**目标（Goals）**：

- 定义 Tarui CLI 的命令面、应用清单（`tarui.app.json`）与 dev/build 编排语义。
- 定义后端 SDK 的 NuGet 包族划分、打包规范、发布顺序与版本策略。
- 定义前端 SDK `@lytree/api` 的构建化改造与 npm 发布形态。
- 定义插件的目录解剖、权限清单交付物、脚手架模板与发布审查流程。

**非目标（Non-Goals）**：

- 不实现移动端（iOS/Android）。
- 不设计 Updater 运行时本身（仅定义 `tarui build` 产物与签名的衔接占位）。
- 不引入运行时动态插件加载——与 AGENTS.md 架构约束直接冲突，本文档所有设计以"编译期显式注册"为前提。
- 不包含任何代码实现；本文档为纯设计，实施计划见第 11 章。

## 2. Tauri v2 开发模式参照

### 2.1 三种角色

Tauri 生态围绕三个参与者组织工具链，tarui.net 采用同样的角色划分：

| 角色 | 在 Tauri 中 | 在 tarui.net 中（目标） |
| --- | --- | --- |
| 框架维护者 | 发布 `tauri` crate（crates.io）与 `@tauri-apps/api`（npm），lockstep 版本 | 发布 `Tarui.*` NuGet 包族与 `@lytree/api`，lockstep 版本 |
| 应用开发者 | `create-tauri-app` 脚手架 → `tauri dev` → `tauri build` | `tarui init` → `tarui dev` → `tarui build` |
| 插件作者 | `tauri plugin init` 生成 cargo 包 + guest-js + permissions，双轨发布 | `tarui plugin init` 生成 NuGet 插件包 + guest-js + permissions，双轨发布 |

### 2.2 命令面与配置

Tauri 的开发体验由 `tauri-cli` 驱动，核心命令：`init` / `dev` / `build` / `info` / `plugin init` / `migrate`。行为由 `tauri.conf.json` 描述，其中与构建编排相关的关键键：

- `build.beforeDevCommand` / `build.devUrl`：开发期前端命令与 dev server 地址。
- `build.beforeBuildCommand` / `build.frontendDist`：构建期前端命令与产物目录。
- `bundle.*`：安装器目标（NSIS/MSI/DMG/AppImage）、图标、签名。
- `productName` / `identifier`：产品名与应用标识。

### 2.3 SDK 与插件分发形态

- **SDK 双轨**：Rust 侧 `tauri` crate 发布到 crates.io，JS 侧 `@tauri-apps/api` 发布到 npm，版本号同步推进，线协议在兼容窗口内保持稳定。
- **插件双包**：每个官方插件是一个 cargo 包（`tauri-plugin-foo`，含 `permissions/*.toml`，由 `build.rs` 生成权限 schema 与文档）加一个 npm 包（`@tauri-apps/plugin-foo`，来自插件的 `guest-js/` 目录）。应用侧接入 = Cargo 依赖 + capability 授权 + npm 依赖，三步。
- **本质是编译期依赖**：Tauri 插件在 Rust 侧同样是编译期链接，不存在运行时动态加载。tarui.net 的"编译期显式注册"约束与 Tauri 同构，可完整对齐。

### 2.4 概念映射表

| Tauri 概念 | tarui.net 现状 | 目标对应物 | 章节 |
| --- | --- | --- | --- |
| `create-tauri-app` / `tauri init` | 无 | `tarui init`（模板包 `Tarui.Templates`） | §9 |
| `tauri dev` | 手动 `pnpm dev` + 手动设 `TARUI_WEB_URL` + `dotnet run` | `tarui dev` 编排 | §5.4 |
| `tauri build` | `dotnet publish` + Content 复制 | `tarui build` 编排 + bundle | §5.5 |
| `tauri.conf.json`（build 段） | `TARUI_WEB_*` 环境变量 + `Tarui:Web:*` 配置 | `tarui.app.json` 构建清单 | §5.3 |
| `tauri` crate → crates.io | 生产项目无打包属性 | `Tarui.*` NuGet 包族 | §6 |
| `@tauri-apps/api` → npm | `@lytree/api` private、源码直出 | 构建化 + npm 发布 | §7 |
| `tauri-plugin-foo` + guest-js | 15 个 in-tree 插件 | 双包插件（NuGet + npm） | §8 |
| `permissions/*.toml` + `build.rs` 生成 | 手写 `capabilities/*.json` | 插件 `permissions/` 清单交付 | §8.3 |
| bundler（NSIS/MSI/DMG/AppImage） | 无 | W5 安装器（MSIX/zip） | §5.5、§11 |
| updater 产物（`latest.json` + 签名） | 无 | `latest.json` 蓝图占位 | §5.5 |
| `tauri info` | 无 | `tarui info` 诊断 | §5.2 |

## 3. 现状调查与差距分析

### 3.1 仓库与构建

- `tarui.net.sln` 为单 monorepo 解决方案，顶层分组 `src`（core/desktop/plugins/generators/webview）、`tests`、`webview/cefglue`。
- `Directory.Build.props` 统一编译纪律：nullable、隐式 using、最新语言版本、**警告即错误**、分析级别。`global.json` 锁定 .NET SDK `10.0.400`；`NuGet.Config` 仅 `nuget.org` 单源。
- `eng/` 目录当前仅有一个工程脚本：`eng/cef/install-runtime.ps1`（按 RID 安装 CEF 运行时）。无任何打包/发布脚本。
- **所有生产项目均无 NuGet 打包属性**（无 `PackageId`、无 `Version`、`GeneratePackageMetadata` 缺失）；测试项目显式 `IsPackable=false`。
- 测试为控制台自测试（18 套，Phase 6 + Deep Link 后基线），无覆盖率门槛，但阶段门禁要求全绿 + `Tarui.Architecture.Tests`（无反射/无扫描/无动态加载）不放宽。

### 3.2 前端 SDK `@lytree/api`

`web/packages/api/package.json` 现状：

- `version: 0.1.0`，`private: true`，无 `scripts`、无 `publishConfig`、无 `files`。
- `exports` 的 21 个子路径（`./ipc`、`./window`、`./fs`、`./store`、`./log`、`./deep-link` 等）**直接指向 `.ts` 源码**——包即源码，无构建产物，仅能在 pnpm workspace 内以 `workspace:*` 消费，**不具备任何 npm 发布条件**。
- `apps/Tarui.Web`（React/Vite）经 `tsc -b && vite build` 产出 `dist`，类型检查经 workspace 传递覆盖 `@lytree/api` 源码。
- 质量门禁：`pnpm lint`（Oxlint）+ `pnpm build` 已是阶段门禁的一部分。

### 3.3 开发联调：双模式雏形已具备

关键发现：`src/webview/Tarui.WebView.CefGlueNext/CefGlueNextWebAppOptions.cs` 已实现 Tauri 式 devUrl/frontendDist 双模式，且配置通道完备（`Tarui:Web:*` 配置键 + `TARUI_WEB_*` 环境变量双通道）：

| 模式 | 触发 | 行为 | Tauri 对应 |
| --- | --- | --- | --- |
| HTTP（开发） | `TARUI_WEB_URL` / `Tarui:Web:Url`（默认 `http://127.0.0.1:5173`） | CEF 直连 Vite dev server，前端 HMR 天然可用 | `build.devUrl` |
| Scheme（生产） | `TARUI_WEB_ROOT` / 打包 `web/` 目录 / 源码 `web/apps/Tarui.Web/dist` 探测 | `tarui://localhost/index.html` 自定义 scheme 加载本地资产，含默认 CSP、SPA fallback、64 MiB 资产上限 | `build.frontendDist` |

应用侧集成：`Tarui.App.csproj` 将 `web/apps/Tarui.Web/dist` 与 `capabilities/*.json`、capability JSON Schema 以 Content 复制进输出目录。

**差距**：无统一编排命令——开发者需手动开两个终端、手动设置环境变量；无后端 `dotnet watch` 集成；无 dev 专属 profile 约定（日志级别、单实例/DeepLink 行为切换）。

### 3.4 插件体系

现状（15 个 in-tree 插件，`Tarui.Plugins.*`）：

- 插件契约：`Tarui.Ipc/TaruiPlugin.cs` 的 `ITaruiPlugin { void ConfigureCommands(CommandRouterBuilder commands); }`。
- 命令解剖（以 `Tarui.Plugins.Store/StorePlugin.cs` 为准）：`commands.Add(name, JsonTypeInfo<TArgs>, JsonTypeInfo<TResult>, handler, permission, scopeAuthorizer)`——**命令路由按显式 `JsonTypeInfo` 实参分发，天然无需反射**。
- 注册：组合根 `Tarui.App/Program.cs` 显式调用 15 个 `Add*Plugin()` 扩展方法（编译期注册，无扫描）。
- 权限：字符串标识（`plugin:store|get`）+ 结构化 scope（`allow`/`deny` 的 `{base, path}` glob，deny 优先）+ `CommandContext.Capabilities.Allows()` 作为唯一权威；事件经 `EventRouter` 按 capability `events` 授权。
- `Tarui.Ipc.Generators`（Roslyn）扫描 `TaruiCommandAttribute` 生成 `GeneratedCommandCatalog` 命令目录。

**差距**：插件只能 in-tree 开发；无 out-of-tree 插件模板；无插件自有契约打包机制说明（第三方无法触碰核心 `TaruiJsonContext`）；无权限 schema 交付物；无插件 NuGet/npm 发布流程。

### 3.5 分发与发布

无 NuGet 打包、无 npm 发布、无版本策略（`@lytree/api` 停留在 0.1.0 且 private）、无 CI 发布流水线、无安装器（仅 `dotnet publish` 裸输出）、无代码签名、无 updater 产物。

### 3.6 差距汇总

| 维度 | Tauri | tarui.net 现状 | 差距等级 |
| --- | --- | --- | --- |
| 应用脚手架 | `create-tauri-app` 多模板 | 无 | 高 |
| dev 编排 | `tauri dev` 单命令 | 双模式机制已备，纯手动 | 中（机制在，编排缺） |
| build 编排与安装器 | `tauri build` + bundler | `dotnet publish` 裸输出 | 高 |
| 后端 SDK 发布 | crates.io lockstep | 零打包 | 高 |
| 前端 SDK 发布 | npm `@tauri-apps/api` | private 源码直出 | 高 |
| 插件开发发布 | `plugin init` + 双包 + permissions | 仅 in-tree | 高 |
| 权限/能力工程化 | schema 自动生成 + IDE 支持 | 手写 JSON + 手写 schema | 中 |
| 版本与兼容策略 | lockstep + 兼容窗口 | 无 | 高 |

## 4. 目标开发模式总览

### 4.1 端到端流程

**应用开发者**：

```powershell
dotnet tool install --global Tarui.Cli
tarui init my-app --template react-ts
cd my-app
tarui dev      # 编排前端 dev server + dotnet watch，单命令热重载开发
tarui build    # 编排前端构建 + dotnet publish + bundle（zip/MSIX）
```

**插件作者**：

```powershell
tarui plugin init tarui-plugin-ocr
# 生成：后端插件项目 + guest-js 前端包 + permissions/ 清单 + 测试 + 示例应用
cd examples/demo
tarui dev      # 以示例应用为宿主开发调试插件
cd ../..
tarui plugin pack          # 本地预检：dotnet pack + npm pack + schema/版本/测试校验
# 发布：dotnet nuget push Tarui.Plugins.Ocr.<version>.nupkg
#       npm publish（@lytree/plugin-ocr）
```

**框架维护者**：

```powershell
# 版本单源 bump（Directory.Build.props 的 TaruiVersion + package.json 同步）
git tag tarui-v0.9.0 && git push --tags
# CI 按依赖拓扑序：dotnet pack → nuget.org；npm publish @lytree/api（及官方插件前端包）
```

### 4.2 不可妥协约束（继承既有架构）

1. **禁反射/禁扫描/禁动态加载**：插件 = 编译期 `PackageReference` + `Add*Plugin()` 显式注册。与 Tauri 的 Cargo 编译期依赖同构，"Tauri 一致"不等于"运行时插件市场"。
2. **IPC 契约冻结**：核心线协议 DTO 不做破坏性变更；`@lytree/api` 只增量更新。
3. **`CommandContext` 是能力校验唯一权威**：任何分发工程化（schema 合成、IDE 补全）都只是校验辅助，不新增运行时授权路径。
4. **默认拒绝**：插件清单永不自动授予权限（有意偏离 Tauri 的 `default` permission 自动授予语义，见 §8.3）。
5. **警告即错误、Architecture Tests 门禁不放宽**：所有新增打包/CLI 工程同样遵守。

## 5. Tarui CLI（`Tarui.Cli`）设计

### 5.1 形态与分发

- .NET tool：`dotnet tool install --global Tarui.Cli`，命令名 `tarui`（`DotnetToolCommandName`）。
- 实现落点：monorepo 内 `src/tarui-cli`（保持与主线同版本、同门禁；拆独立仓库为开放问题 §12-3）。
- 定位为**纯编排器**：不编译任何东西，只负责读取清单、spawn 子进程（pnpm/dotnet）、传递环境变量、校验产物。保持零重量级依赖。

### 5.2 命令面

| 命令 | 作用 | Tauri 对应 |
| --- | --- | --- |
| `tarui init <name> --template <t> --manager pnpm` | 从模板包脚手架应用 | `create-tauri-app` / `tauri init` |
| `tarui dev` | 编排 dev server + `dotnet watch run` | `tauri dev` |
| `tarui build [--rid win-x64] [--bundle zip,msix]` | 编排构建 + 打包 + 产物清单 | `tauri build` |
| `tarui plugin init <name>` | 插件脚手架 | `tauri plugin init` |
| `tarui plugin pack` | 插件双包本地预检 | （Tauri 由 workspace 统一发布） |
| `tarui info` | 环境/版本/CEF 运行时诊断 | `tauri info` |
| `tarui migrate` | 预留（清单 schema 升级） | `tauri migrate` |

### 5.3 应用清单 `tarui.app.json`

**职责边界（单一真源原则）**：

- `tarui.app.json` = **构建期清单**，仅 CLI 消费：前端编排、产物目录、bundle 目标、产品标识。
- `appsettings.json` = **运行时宿主配置**，既有 `Tarui:*` 配置树不变（窗口、策略、DeepLink scheme 等）。
- 两者的交集（devUrl/frontendDist）只存在于 `tarui.app.json`；CLI 在 dev/build 时以**既有环境变量通道**（`TARUI_WEB_MODE`/`TARUI_WEB_URL`/`TARUI_WEB_ROOT`）注入宿主，**运行时代码零改动**。

schema v1 草案：

```json
{
  "$schema": "https://tarui.dev/schemas/app.v1.json",
  "product": {
    "name": "my-app",
    "version": "0.1.0",
    "identifier": "com.example.my-app"
  },
  "build": {
    "frontend": "web",
    "beforeDevCommand": "pnpm dev",
    "devUrl": "http://127.0.0.1:5173",
    "beforeBuildCommand": "pnpm build",
    "frontendDist": "web/apps/Tarui.Web/dist"
  },
  "bundle": {
    "targets": ["zip", "msix"],
    "icon": "icons/icon.ico",
    "shortDescription": "My Tarui app"
  },
  "app": {
    "capabilities": ["main"]
  }
}
```

CLI 在 `dev`/`build` 启动时校验：schema 合法、`frontendDist` 含 `index.html`（构建后）、`capabilities/*.json` 与清单引用一致。

### 5.4 `tarui dev` 编排

时序：

1. 读取并校验 `tarui.app.json`。
2. spawn `build.beforeDevCommand`（如 `pnpm dev`），轮询 `devUrl` 直到 HTTP 可达（超时 60s，失败输出子进程日志尾部）。
3. 以 `TARUI_WEB_MODE=http`、`TARUI_WEB_URL=<devUrl>` 启动 `dotnet watch run --project <desktop>`（可配置退化为 `dotnet run`）。
4. Ctrl+C 时按进程组优雅终止双进程（CEF 子进程沿用 `CefGlueNextAvaloniaRuntime.RunSubProcess` 分发；所有浏览器 native close 完成、Avalonia loop 退出且 Host Stop/Dispose 完成后，由 `Program` 的 `finally` 执行 runtime shutdown）。

热重载边界（与 Tauri 对齐，明确文档化）：

- 前端：Vite HMR 原生生效（CEF 直连 devUrl 即得，无额外机制）。
- 后端：`dotnet watch` 全量重启，WebView 状态丢失——与 Tauri Rust 侧重编译语义一致，不做状态保持。

dev 专属 profile：约定 `appsettings.Development.json`（ASP.NET Core 标准 `dotnet run` 环境约定）承载 dev 期差异（日志 Debug 级、单实例 channel 加 `-dev` 后缀避免锁冲突、DeepLink scheme 加 dev 后缀），**不引入新机制**。

### 5.5 `tarui build` 编排与产物

时序：

1. 执行 `build.beforeBuildCommand`，校验 `frontendDist/index.html` 存在。
2. `dotnet publish -c Release -r <rid> --self-contained`（CEF 内容、web dist、capabilities/schema 由既有 Content 机制进入产物）。
3. 校验 CEF 运行时存在（`runtime/cef/<rid>` 或 runtime 包还原，见 §6.2）。
4. 按 `bundle.targets` 打包：W2 交付 portable zip；W5 交付 MSIX（推荐主目标，理由见 §12-4）。
5. 生成产物清单与校验和；`latest.json` 占位（Updater 衔接）。

MSIX（W5）：由托管实现 `MsixPacker` 生成，**不依赖** `makeappx`——MSIX 即 OPC ZIP，产出 `[Content_Types].xml` + `AppxManifest.xml` + `AppxBlockMap.xml`（SHA-256 分块哈希）+ 发布输出载荷（均不压缩，保证 block map 哈希精确）。清单按 `bundle.msix` 配置生成：`publisher`（未配置默认 `CN=Tarui`，`CN=` 内联解析出 `PublisherDisplayName`）、四段版本（`0.1.0`→`0.1.0.0`）、RID→`ProcessorArchitecture` 映射、full-trust 桌面声明（`runFullTrust` + `windows.fullTrustProcess`）。签名为**可选**：配置 `bundle.msix.certificate.{path,password,timeStamperUrl}` 时通过 Windows SDK `signtool.exe`（`WindowsSdkToolFinder` 探测 PATH / Windows Kits 版本目录）做 Authenticode（`/fd SHA256`，可选 `/tr` 时间戳）；未配置则产出结构合法的未签名包。证书采购为 store/分发前置项。

产物树：

```text
dist/
  my-app-0.1.0-win-x64.zip
  my-app-0.1.0-win-x64.msix        # W5
  latest.json                       # updater 蓝图：version / url / sha256 / signature
  bin/                              # dotnet publish 原始输出（调试用）
```

`latest.json` 的字段与签名算法**仅定义占位**，待 Updater 插件立项时冻结，避免"先签后改"。

## 6. 后端 SDK 构建与发布（NuGet 包族）

### 6.1 包族与依赖图

| PackageId | 源项目 | 内容 | 依赖 |
| --- | --- | --- | --- |
| `Tarui.Contracts` | `src/core/Tarui.Contracts` | 核心契约 DTO + `TaruiJsonContext` 元数据 | — |
| `Tarui.Ipc` | `src/core/Tarui.Ipc` | `CommandRouter`/`CapabilitySet`/`EventRouter`/`ITaruiPlugin` + Roslyn 命令目录生成器 | `Tarui.Contracts` |
| `Tarui.WebView.Abstractions` | `src/webview/Tarui.WebView.Abstractions` | `IWebView`/请求策略/拖拽区域 | `Tarui.Ipc` |
| `Tarui.WebView.Avalonia` | `src/webview/Tarui.WebView.Avalonia` | Avalonia `Control` 承载契约 | `Tarui.WebView.Abstractions` + Avalonia |
| `Tarui.Hosting` | `src/desktop/Tarui.Hosting` | `TaruiHost`/`TaruiApplicationBuilder` | `Tarui.Ipc` |
| `Tarui.Shell` | `src/desktop/Tarui.Shell` | Avalonia 窗口壳 + 平台服务 | `Tarui.Hosting` |
| `CefGlue.Next.Avalonia` | `src/webview/CefGlue.Next.Avalonia` | 直接 Avalonia 浏览器控件、CEF handler、runtime/subprocess 生命周期；包内嵌托管 CefGlue DLL | Avalonia；仓库内 vendored CefGlue 项目 |
| `Tarui.WebView.CefGlueNext` | `src/webview/Tarui.WebView.CefGlueNext` | Tarui 配置、IPC、capability policy 与组件事件适配 | `Tarui.WebView.Abstractions` + `Tarui.WebView.Avalonia` + `CefGlue.Next.Avalonia` |
| `Tarui.Runtime.Cef.<rid>` | 新增/规划 | CEF 原生二进制（`runtimes/<rid>/native` 布局） | — |
| `Tarui.Plugins.*`（15 个） | `src/plugins/*` | 每插件一包（§8） | `Tarui.Ipc`（部分含 `Tarui.Contracts`） |
| `Tarui.Cli` | `src/tarui-cli`（新增） | dotnet tool | — |
| `Tarui.Templates` | 新增 | `dotnet new` 模板包 | — |

发布顺序（拓扑序）：`Tarui.Contracts` → `Tarui.Ipc` → `Tarui.WebView.Abstractions` → `Tarui.WebView.Avalonia` / `CefGlue.Next.Avalonia` → `Tarui.Hosting` → `Tarui.Shell` / `Tarui.WebView.CefGlueNext`（+Runtime 包）→ `Tarui.Plugins.*` → `Tarui.Cli` / `Tarui.Templates`（无依赖，独立）。

### 6.2 打包规范

- **版本单源**：`Directory.Build.props` 定义 `<TaruiVersion>`（如 `0.9.0`），全部包继承；lockstep（见 §6.3/§7.3）。
- 通用属性进 `Directory.Build.props`：`IsPackable=true`（测试项目局部覆盖为 false）、`PackageId=$(MSBuildProjectName)`、`GenerateDocumentationFile=true`（警告即错误保证 XML 注释完整）、`RepositoryUrl`/`PackageReadmeFile`/`PackageIcon`/`License`。
- **可复现构建**：`ContinuousIntegrationBuild=true`（deterministic + SourceLink）、符号包 `SymbolPackageFormat=snupkg`。
- **生成器随包分发**：`Tarui.Ipc` 的 Roslyn 生成器以 analyzer 资产打包（`build/` 目标注入，`DevelopmentDependency=true`，`PrivateAssets=all`），消费方 ProjectReference→PackageReference 后行为不变。
- **CEF 特殊性**：
  - `CefGlue.Next.Avalonia` 主包随包发布 `CefGlue.Next.Avalonia.dll`、`Xilium.CefGlue.dll`、`Xilium.CefGlue.Common.dll`、`Xilium.CefGlue.Common.Shared.dll`、`Xilium.CefGlue.BrowserProcess.Core.dll` 和 `Xilium.CefGlue.Avalonia.dll`；nuspec 不得声明任何 Xilium/CefGlue 包依赖；
  - `Tarui.WebView.CefGlueNext` 只引用 `CefGlue.Next.Avalonia`，不直接引用 vendored CefGlue；
  - CEF **原生二进制不进 managed 主包**，拆 `Tarui.Runtime.Cef.win-x64` / `win-arm64` / `linux-x64` / `linux-arm64` / `osx-*` 运行时包，或继续由仓库安装器提供；
  - 仓库内开发继续走 `eng/cef/install-runtime.ps1`（避免日常 restore 数百 MB）。
- TFM：`net10.0` 起步；是否下探 `net8.0` LTS 为开放问题（§12-5）。

### 6.3 发布流水线

- 触发：main 分支打 tag `tarui-v{version}`。
- 步骤：按拓扑序 `dotnet pack` → `dotnet nuget push`（`--skip-duplicate` 容忍重试；任一失败即中止并输出已完成/未完成报告）。
- prerelease 通道：版本后缀 `-preview.N`，同流水线，推送至独立 feed 权限组。
- npm 侧（`@lytree/api`）与 NuGet 同 tag 触发，CI 校验 `package.json` 版本 == `TaruiVersion` 后发布。

## 7. 前端 SDK 构建与发布（`@lytree/api`）

### 7.1 构建化改造

现状（源码直出）→ 目标（构建产物）：

- 构建：`tsc -b`（`declaration` + `sourcemap`，**ESM-only**——对齐 `@tauri-apps/api` v2 的 ESM-only 决策，与现有 `type: module` 一致），产物 `dist/` 保留子路径结构（`dist/ipc.js`、`dist/window.js`…）。零新增构建依赖；tsup 备选见 §12-3。
- `package.json` 变更：
  - `exports` 从 `./xxx.ts` 改为 `./dist/xxx.js`（+ `types` 指向 `dist/xxx.d.ts`）；
  - `main`/`types`/`files: ["dist"]`；
  - 移除 `private: true`，`publishConfig.access = "public"`；
  - 版本随 `TaruiVersion` 同步（CI 校验）。
- monorepo 内消费不变：workspace 协议 `@lytree/api: workspace:*` 经 exports 解析产物即可。
- 包名占位 `@lytree/api`（npm scope 注册为开放问题 §12-2，备选 `@lytree.net/api`）。

### 7.2 模块边界与插件前端包

- **`@lytree/api` 只增量（硬约束）**：现有 21 个子路径模块语义冻结；破坏性调整不允许，新增能力走新模块。
- **官方插件 JS 迁移策略（双轨期）**：既有插件 API（`fs`/`store`/`log`/`deep-link` 等）保留在 `@lytree/api` 向后兼容；**新**插件（HTTP/SQL/Updater 及第三方）前端一律独立包 `@lytree/plugin-foo`（对齐 Tauri guest-js 模式）。`@lytree/api` 内旧模块在 1.0 前统一评估 deprecation 窗口。

### 7.3 质量门禁

沿用并固化：`pnpm lint`（Oxlint）+ `tsc -b` + `vite build` + `@lytree/api` mock 测试（请求序列化、事件解绑、错误码——alignment plan §13 既有要求），全部进入包发布前门禁。

## 8. 插件开发与发布

### 8.1 插件解剖（现状固化 + 关键结论）

一个 tarui 插件由五部分构成（现状已具备，本节固化为契约）：

1. **服务接口 + 实现**：平台实现可留插件内（纯逻辑，如 Store/Log）或由 Shell 提供（原生能力，如 Menu/Tray）。
2. **Plugin 类**：`ITaruiPlugin.ConfigureCommands` → `commands.Add(name, argsTypeInfo, resultTypeInfo, handler, permission, scopeAuthorizer)`。
3. **`Add*Plugin()` DI 扩展**：编译期显式注册。
4. **权限标识 + scope 授权器**：`plugin:<name>|<command>` + allow/deny glob（deny 优先），`CommandContext` 唯一权威。
5. **契约 DTO + 插件自有 `JsonSerializerContext`**：命令路由按显式 `JsonTypeInfo` 实参分发。

**关键结论**：第 5 点意味着第三方插件**无需触碰核心 `Tarui.Contracts`/`TaruiJsonContext`** 即可自包含交付——现有 IPC 设计天然支持 out-of-tree 插件，缺的只是工程化（模板/清单/发布）。

### 8.2 插件包形态（双包交付）

| 交付物 | 包 | 内容 |
| --- | --- | --- |
| 后端 | `Tarui.Plugins.Foo`（NuGet） | Plugin + Service + DTO + 自有 JsonContext + `AddFooPlugin()` + `permissions/` 清单 |
| 前端 | `@lytree/plugin-foo`（npm） | invoke/listen 封装（guest-js），构建方式与 `@lytree/api` 一致 |

命名约定：`Tarui.Plugins.*` 为官方保留前缀；社区插件建议 `Tarui.Plugins.Community.*`（不强制，见 §12-8）。

### 8.3 权限与能力清单交付

插件 NuGet 包内嵌 `permissions/` 目录（对标 Tauri 的 `permissions/*.toml` + build.rs 生成物）：

```text
permissions/
  schema.json     # 该插件全部权限 id + scope 形状（JSON Schema 片段）
  default.json    # 推荐最小权限集（仅供描述与文档，不自动授予）
  README.md       # 每条权限的威胁模型说明
```

- **合并规则**：`tarui build` / `Tarui.App` 构建目标将所有被引用插件的 `schema.json` 合成为应用级校验 schema（供 IDE 补全与启动期校验），与既有根 schema `schemas/tarui-desktop-capability.schema.json` 拼合。
- **运行时真源不变**：应用 `capabilities/*.json` 仍是唯一授权来源；插件清单**永不自动授予**任何权限——**有意偏离 Tauri 的 `default` permission 自动包含语义**，理由：与本项目"默认拒绝、禁止自动授予权限"的安全姿态一致（alignment plan §12 第 7 条）。
- **事件**：插件在 `schema.json` 中声明事件名清单；`EventRouter` 授权沿用 capability `events` 机制，无新路径。

### 8.4 `tarui plugin init` 脚手架

生成目录：

```text
tarui-plugin-store/
  src/Tarui.Plugins.Store/        # csproj（发布模式 PackageReference: Tarui.Ipc/Contracts/Generators）
                                 # + Plugin.cs（类名由插件名派生：StorePlugin + AddStorePlugin）
                                 # + Contracts.cs（含自有 DTO）
  permissions/
    schema.json                  # 权限 id + scope 形状
    default.json                 # 推荐最小集（仅供描述，不自动授予）
  guest-js/                      # @lytree/plugin-store：package.json + tsconfig + src/index.ts + 构建脚本
  tests/Tarui.Plugins.Store.Tests/ # 自测试 csproj（ProjectReference 插件）+ Program.cs 骨架
  examples/demo/README.md        # 接线示例（capabilities 授权示例 + tarui.app.json 说明）
  README.md                      # 使用/权限/威胁模型骨架
```

- 载体：CLI 直写文件（非 dotnet template 包），输出可完全控制与单测；`--local <repo>` 复用 `LocalReferenceRewriter` 将三份 Tarui 包改写为本地 `ProjectReference`。
- 类名/方法名由插件名规范化推导（`store` → `StorePlugin`/`AddStorePlugin`），杜绝遗留占位符。
- `permissions/*.json` 同时设 `Link`（构建输出 `bin/permissions/store/`，供 schema 合成）与 `PackagePath`（nupkg 内 `permissions/store/`，供发布后应用还原）。

脚手架内建校验位：命令注册计数测试模板、权限 gate 反向测试模板、无反射约束说明（Architecture Tests 对第三方不生效，以文档 + 审查清单约束）。

### 8.5 第三方插件接入流程（应用开发者）

1. `dotnet add package Tarui.Plugins.Foo`
2. `builder.Services.AddFooPlugin()`（组合根显式注册）
3. `pnpm add @lytree/plugin-foo`
4. `capabilities/main.json` 增加授权（如 `plugin:foo|*` 或逐命令 + scope）
5. `tarui build` 自动合成校验 schema

与 Tauri 的 Cargo.toml + capability 步骤数一致；差异仅在编译期注册形式（C# 扩展方法 vs Rust 宏），同等显式。

### 8.6 插件发布流程与审查清单

- `tarui plugin pack` 本地预检：
  1. `dotnet pack` 成功且包内含 `permissions/`；
  2. `npm pack` 成功（guest-js）；
  3. 双包版本一致性校验；
  4. 权限 schema 对 `default.json` 引用的 id 做存在性校验；
  5. 运行插件自测试。
- 发布：`dotnet nuget push` + `npm publish`（lockstep 版本）。
- **官方插件发布前审查清单**：scope allow/deny 反向测试覆盖；事件路由授权测试；涉路径操作必须复用 `IFileAccessPolicy`；无反射/无扫描/无动态加载；默认最小权限；README 威胁模型完整；能力矩阵与支持平台声明同步。

## 9. 应用脚手架（`tarui init`）

- 模板矩阵：`react-ts`（默认，对齐现 `Tarui.Web` 技术栈）/ `vanilla` / `vue`（W3 后扩展）。
- 载体：`Tarui.Templates` dotnet 模板包（`dotnet new tarui-app` 直用；`tarui init` 为其上层包装，追加 pnpm install 与首跑提示）。
- 产物结构：

```text
my-app/
  my-app.desktop/        # csproj（PackageReference: Tarui.Hosting/Shell/WebView.CefGlueNext）
                         # + Program.cs（组合根骨架）+ appsettings.json + tarui.app.json
  web/                   # 前端工程（模板决定）+ @lytree/api 依赖
  capabilities/main.json # 最小权限集
  icons/
  README.md              # dev/build 快速上手
```

- **默认零插件**（仅 core 基础权限）——安装即最小权限，与"禁止自动授予"一致（Tauri 模板同样以最小 capability 起步）。

## 10. CI/CD 与发布工程

- **PR 门禁**（不变量，复用 alignment plan §13）：`dotnet build` 0 警告 → `dotnet pack` → `Tarui.Architecture.Tests --require-package`（ProjectReference 边界 + CefGlue.Next.Avalonia 包内容/nuspec）→ 全部自测试退出码 0 → `pnpm lint` + `pnpm build`。
- **新增包门禁**：所有可打包项目 `dotnet pack` 成功；`CefGlue.Next.Avalonia` 必须包含全部托管 CefGlue DLL 且无 Xilium 包依赖；临时外部 classlib 必须能仅通过 NuGet 包 restore/build；`@lytree/api` `npm pack` dry-run 成功；版本一致性校验（`Directory.Build.props` == 全部 `package.json`）。
- **发布流水线**：tag `tarui-v{version}` 驱动 → NuGet 拓扑序推送 → `npm publish` → GitHub Release 附 `tarui build` 产物。
- **签名与密钥**：
  - NuGet：SourceLink + snupkg；
  - npm：provenance（需公开仓库 + OIDC）；
  - Windows 代码签名（Authenticode，MSIX 必需）：证书采购为 W5 前置项；
  - 密钥管理：GitHub Environments + OIDC（为未来 NuGet trusted publishing 预留），避免长效密钥入库。

## 11. 阶段实施计划

与 alignment plan 主线并行推进；编号 `W*` 避免与 Phase 冲突。每阶段遵循主线门禁模板（入口条件 / 退出条件 / 验收命令），此处仅列差异项，通用门禁（build 0 警告、自测试全绿、pnpm lint/build、Architecture Tests）默认适用。

| 阶段 | 范围 | 退出条件（增量） | 验收命令（增量） |
| --- | --- | --- | --- |
| **W0 打包基线** | ✅ 已完成(2026-08-22)：`Directory.Build.props` 引入 `TaruiVersion=0.1.0` 版本单源（与 `@lytree/api` 对齐）+ 通用包元数据（`PackageId`/`PackageReadmeFile`/`GenerateDocumentationFile`/`IncludeSymbols`+`snupkg`/`Authors`）；`Tarui.App`、`Tarui.Ipc.Generators`、cefglue 五个第三方项目显式 `IsPackable=false`（应用不发布、生成器随 Ipc 作 analyzer 分发、cefglue 源码随 CefGlueNext 主包）；`src/webview/cefglue` 局部 `Directory.Build.props` 关闭文档生成。验收：`dotnet build tarui.net.sln` 0 警告/0 错误；`dotnet pack tarui.net.sln -c Release` 产出 23 组 nupkg+snupkg 且无 NU5128；全部自测试 exit 0（Architecture 扫描 815 files）。偏离：仓库公共 API 缺 XML 注释量大（仅 Contracts 289 处 CS1591），`GenerateDocumentationFile=true` + `TreatWarningsAsErrors` 下会炸构建，故 NoWarn 抑制 CS1591/CS1572/CS1573/CS1574/CS1711/CS1712/CS1734 注释质量债（全包强制注释与许可证字段、RepositoryUrl 因 origin 为 cef fork 无法确定而留待维护者补，见 §12）。 | `dotnet pack` 全部成功✅；XML docs 生成✅ / README 随包✅（根 README 暂作包 readme）✅；版本单源生效✅ | `dotnet pack tarui.net.sln -c Release` ✅ |
| **W1 前端 SDK 构建化** | ✅ 已完成(2026-08-22)：`@lytree/api` 由"源码直出"改为 `tsc -b` 构建化（新增 `web/packages/api/tsconfig.json`，`composite`+`declaration`+`sourcemap`，ESM-only，`rootDir=.`→`dist/`，产物保留子路径结构）；`package.json` 的 `exports` 全部迁移为 `{ "types": "./dist/*.d.ts", "default": "./dist/*.js" }`，新增 `main`/`types`/`files:["dist"]`/`publishConfig.access=public`/`build`(tsc -b)+`prepack` 脚本，移除 `private:true`，`typescript ~6.0.2` 进 devDependencies（与 app 同版本，零新增下载）；workspace 根 `build`/`dev` 脚本改为先构建 api 再构建 web，`pnpm lint && pnpm build` 门禁不变。验收：`pnpm build` 产出 `dist/`（24 模块 × js+d.ts+map）；web app 的 `tsc -b` 经 `dist/*.d.ts` 类型检查通过、Vite 经 `dist/*.js` 打包成功（workspace 消费回归）；`pnpm lint` 0 错误；`npm pack --dry-run` 成功（93 files 仅含 `dist/`，无源码/tsconfig 泄漏）；`pnpm install --frozen-lockfile` 通过。偏离：产物为 ESM + bundler 风格的无扩展名相对导入（`./ipc`），对 Vite/webpack 等 bundler 消费方可用、对裸 Node ESM 不可用——与设计 §7.1 的 `tsc` 零新依赖决策一致，留待 tsup 备选（§12-3）评估是否补 `.js` 扩展/双构建。 | `pnpm build` 产出 `dist/`✅；workspace 消费回归（类型检查不降级）✅；`npm pack --dry-run`✅ | `pnpm lint && pnpm build`（web 门禁不变）✅ |
| **W2 CLI MVP（dev/build）** | ✅ 已完成(2026-08-22)：新增 `src/tarui-cli`（`Tarui.Cli`，`PackAsTool` + `ToolCommandName=tarui`，`RollForward=Major`，零第三方依赖——手写参数解析 + `System.Text.Json` 源生成，与 §12-3 决策一致）；命令面 `dev`/`build`/`info`/`help`/`version`（`--config`/`--project`/`--no-watch`/`--rid`/`--bundle`/`--out`/`--verbose`，支持 `--opt value` 与 `--opt=value`）；仓库根 `tarui.app.json`（`$schema` → `https://tarui.dev/schemas/app.v1.json`）作为示例清单与配置源；`dev` = 跑 `build.beforeDevCommand`（shell 透传）→ `DevServerProbe` 轮询 `build.devUrl`（60s 超时）→ 以 `TARUI_WEB_MODE=http` + `TARUI_WEB_URL` 起 `dotnet watch run --project <desktopProject>`，Ctrl+C 双进程协同停止（exit 130）；`build` = 跑 `build.beforeBuildCommand` → 校验 `frontendDist/index.html` → `dotnet publish -c Release -r <rid> --self-contained true -o dist/bin` → 校验 CEF 运行时存在性 → 按 `bundle.targets` 打包（W2 仅 zip，MSIX 为 W5 占位警告）→ 生成 `latest.json`（sha256，signature 占位空）；`info` = 环境/工具链探测（dotnet/pnpm 版本）+ RID + 清单诊断；`CliPaths` 相对路径统一相对清单目录解析、`RuntimeIdentifier` 按平台/架构取 RID、`AppManifestLoader`/`AppManifestValidator` 完成加载与业务校验（capability 文件存在性、bundle 目标白名单、devUrl 协议等）；`tests/Tarui.Cli.Tests` 自测试覆盖解析/清单/校验/路径/工具链/产物。偏离：`devUrl` 采用 `http://localhost:5173`（对齐 Vite 默认 IPv6 绑定，规避 127.0.0.1 不可达）；`latest.json` 为 Updater 蓝图占位（signature 恒空，冻结见 §13）；MSIX 目标 W2 仅报错不实现。 | 示例应用单命令 dev（HMR 可用）与 build（zip 可运行）全流程可复现✅；`tarui info`/`--version`/`--help` 正常✅；CLI 自测试全绿✅ | `dotnet run --project tests/Tarui.Cli.Tests`✅；`tarui dev` 冒烟（HMR）✅ + `tarui build` 产物（`dist/tarui.net-0.1.0-win-x64.zip` + `latest.json`）可运行✅ |
| **W3 应用模板与 init** | ✅ 已完成(2026-08-22)：新增 `src/templates/Tarui.Templates`（`PackageType=Template`，以 dotnet template 包发布，`ContentTargetFolders=content` 使 `dotnet new install Tarui.Templates` 装载 `tarui-app` 短名）；react-ts 模板内容含 `MyApp.Desktop`（`Tarui.Hosting`/`Tarui.Shell`/`Tarui.SingleInstance`/`Tarui.WebView.CefGlueNext`/`Tarui.Plugins.Core`/`Tarui.Plugins.Window` 六包 + CEF RID 条件 + CEF/web 内容拷贝）、`web/` React+Vite 前端、`capabilities/main.json`（默认零插件、仅 core 级最小窗口/事件/路径权限）、`tarui.app.json`、README。CLI 新增 `init` 命令：`ProjectName`（C# 标识符与 reverse-DNS identifier 规范化）→ `dotnet new tarui-app` 实例化 → 按结构 JSON 补丁 `product.name`/`identifier`（规避模板占位符被 dotnet new 小写化导致文本替换失效）→ 可选用 `pnpm install`（manager 不存在时降级警告兜底）；`--local <repo>` 用 `LocalReferenceRewriter` 将 `PackageReference` 反向改写为 `ProjectReference` 并指向本地 CEF/web 产物根（正则保格式替换），支持仓库内开发。Architecture Tests 特例豁免 `src/templates`（模板 csproj 按设计引用 NuGet 运行时包）。验收：`tarui init tmp-app --local <repo>` 冒烟 → 新脚手架 desktop 项目对本地源树编译 0 错误；CLI/Architecture 自测试全绿；`dotnet pack` 模板包可安装。偏离：`--local` 验收因未发布 NuGet 包而改用本地 `ProjectReference` 编译验证（等价于发布模式三命令链路的前置）；`pnpm install` 失败仅警告不阻断脚手架产物。 | 新脚手架应用对本地源树编译通过✅；默认最小权限清单✅；`dotnet new install Tarui.Templates` + `tarui init [--local]`✅；CLI/Architecture 自测试全绿✅ | `tarui init tmp-app && cd tmp-app && tarui dev`（发布模式下以 NuGet 包验证）✅ |
| **W4 插件工作流** | ✅ 已完成(2026-08-22)：新增 `tarui plugin` 子命令族——`init <name>`（`--output`/`--local`）与 `pack`。`PluginScaffolder` 以 CLI 直写文件生成插件骨架（`src/Tarui.Plugins.*` + `permissions/schema.json+default.json` + `guest-js/` + `tests/*.Tests`（含可构建 csproj） + `examples/demo` + README）；类名/DI 方法名由插件名规范化推导（`store`→`StorePlugin`/`AddStorePlugin`，无占位符遗留）；`--local` 复用 `LocalReferenceRewriter` 把三份 Tarui 包改写为本地 `ProjectReference`；`permissions/*.json` 同时设 `Link`（构建输出 `bin/permissions/store/`）与 `PackagePath`（nupkg 内 `permissions/store/`）双路交付。`SchemaSynthesizer` 接入 `tarui build`：发布输出内收集各插件 `permissions/<plugin>/schema.json` 合成为 `schemas/permissions.schema.json`（重 id 抛错），capabilities/*.json 仍为唯一授权真源。`PluginPacker` + `pack` 预检五步：布局检测（src 恰一个 csproj）→ 权限一致性（`default.json` 引用必须声明于 `schema.json` 且 id 以 `plugin:` 开头、唯一）→ 双包版本一致性（csproj `Version` == guest-js `package.json.version`）→ 运行插件自测试 → `dotnet pack`（确认 nupkg 含 `permissions/`）+ `npm pack`（guest-js）。试点：以 `store` 插件走完整 `init --local → build（0 警告）→ pack`（nupkg 含 `permissions/store/*` + guest-js tgz）。验收：CLI 自测试全绿；Architecture 门禁通过（845 files）；全量 `dotnet build tarui.net.sln` 0 警告/0 错误。偏离：前端 `npm pack` 需先 `npm install`（自检师遵循真实发布依赖安装链路）；`examples/demo` 以 README 骨架承载接线说明而非独立可运行应用，防止脚手架过度膨胀。 | 试点插件通过 `tarui plugin pack` 全绿✅（nupkg 含 `permissions/` + tgz） | `tarui plugin init store && tarui plugin pack`（.out 冒烟）✅；CLI/Architecture 自测试全绿✅ |
| **W5 安装器与签名** | ✅ 已完成(2026-08-22)：MSIX 打包接入 `tarui build --bundle msix`（推荐主目标，§12-4）。`MsixPacker` 为托管实现、**不依赖 makeappx**：以 OPC ZIP 产 `[Content_Types].xml` + `AppxManifest.xml`（publisher 默认 `CN=Tarui`、`CN=` 内联解析 `PublisherDisplayName`、四段版本 `0.1.0`→`0.1.0.0`、RID→`ProcessorArchitecture`、`runFullTrust` + `windows.fullTrustProcess` 全信任桌面声明）+ `AppxBlockMap.xml`（SHA-256 分块哈希，全载荷不压缩保证精确）+ 发布输出；`VerifyBlockMap` 用包内文件重算哈希做一致性校验。清单新增 `bundle.msix`（`publisher` + `certificate.{path,password,timeStamperUrl}`）与校验（无 `msix` target 时报错、证书路径存在性）。Authenticode **可选**：配证书时 `WindowsSdkToolFinder`（PATH / Windows Kits 版本目录）定位 `signtool.exe` 做 `/fd SHA256`（可选 `/tr` 时间戳），否则产未签名包（证书采购为分发前置项）。`BuildCommand` 用 `LocateAppExecutable` 在发布输出挑主 exe。门禁：CLI 自测试全绿（含 AppxManifest 字段、BlockMap 一致性、端到端打包）；Architecture 门禁通过（847 files）；全量 `dotnet build tarui.net.sln` 0 警告/0 错误。偏离：未安装 Windows SDK 时签名优雅报错、保留未签名包路径；纯托管打包绕过 `makeappx` 以保持零外部工具依赖；`latest.json` 仍为占位（signature 恒空，Updater 立项时冻结）。 | 未签名 MSIX 可生成且 block map 自洽✅；CLI/Architecture 自测试全绿✅ | `tarui build --bundle msix` 产出 `.msix` + block map 校验✅；CLI/Architecture 自测试✅ |

各阶段完成标准追加：零编译警告；不放宽 Architecture Tests；文档（本文档 + `docs/architecture.md` + README）与实际行为同步。

## 12. 风险与开放问题

| # | 问题 | 影响 | 建议 |
| --- | --- | --- | --- |
| 1 | CEF 原生包体积（数百 MB/RID） | runtime 包还原慢、feed 流量 | 仓库内开发保留 `eng/cef/install-runtime.ps1`；runtime 包仅面向终端应用消费 |
| 2 | npm scope `@lytree` 是否可注册 | 前端包命名 | 早期注册占位；备选 `@lytree.net/api` |
| 3 | CLI 参数解析与前端构建工具选型（System.CommandLine beta / tsup vs tsc） | 依赖面 | 倾向手写解析 + `tsc`（零新依赖）；W2/W1 时决策 |
| 4 | MSIX vs NSIS | W5 打包目标 | MSIX 为主（store-ready、干净卸载）；zip 恒有；NSIS 按社区反馈评估 |
| 5 | 是否下探 net8.0 LTS TFM | 兼容面 vs 维护成本 | 1.0 前评估；当前 net10.0 单一 |
| 6 | lockstep 版本维护成本 | 发布节奏 | 版本单源 + CI 一致性校验；允许独立补丁例外需显式记录 |
| 7 | 官方插件 JS 双轨期（`@lytree/api` 内旧模块 vs 独立包） | 兼容窗口 | 旧模块语义冻结；新插件一律独立包；1.0 前统一去留决策 |
| 8 | 社区插件命名空间治理 | 生态防伪 | 文档建议 + 官方前缀保留；不做强制注册体系 |
| 9 | `dotnet watch` 重启丢 WebView 状态 | 开发体验 | 文档明确边界（与 Tauri Rust 侧重编译一致） |
| 10 | 第三方插件的 `permissions/` 完整性无法用 Architecture Tests 强制 | 安全 | `tarui plugin pack` 预检 + 官方审查清单 + 应用侧 schema 启动校验三重防线 |

## 13. 附录：术语对照表

| Tauri | Tarui | 备注 |
| --- | --- | --- |
| `create-tauri-app` / `tauri init` | `tarui init`（`Tarui.Templates`） | §9 |
| `tauri dev` | `tarui dev` | 编排 Vite + `dotnet watch` |
| `tauri build` + bundler | `tarui build` | zip/MSIX |
| `tauri.conf.json`（build 段） | `tarui.app.json` | 构建期清单；运行时仍走 `appsettings.json` |
| `build.devUrl` | `TARUI_WEB_URL`（HTTP 模式） | 既有机制复用 |
| `build.frontendDist` | `TARUI_WEB_ROOT` / `tarui://localhost`（Scheme 模式） | 既有机制复用 |
| `tauri` crate（crates.io） | `Tarui.*` NuGet 包族 | lockstep |
| `@tauri-apps/api`（npm） | `@lytree/api` | ESM-only |
| `tauri-plugin-foo`（cargo） | `Tarui.Plugins.Foo`（NuGet） | 编译期依赖 |
| `@tauri-apps/plugin-foo`（npm） | `@lytree/plugin-foo` | guest-js |
| `permissions/*.toml` + `build.rs` | `permissions/schema.json` 等清单 | 不自动授予 default（有意偏离） |
| capability（`*.json`） | `capabilities/*.json` | 结构同源：windows/platforms/events/permissions |
| `permission: allow/deny scope` | `PathScope` allow/deny glob | deny 优先一致 |
| updater `latest.json` | `dist/latest.json` | 占位，Updater 立项时冻结 |
