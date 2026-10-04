# Repository Guidelines

## 项目结构与模块组织

主解决方案为 `tarui.net.slnx`。生产代码位于 `src/`：`core/` 存放契约与无反射 IPC，`desktop/` 存放 Hosting、Shell 和应用宿主，`plugins/` 存放原生能力插件，`generators/` 存放 Roslyn 生成器，`webview/` 存放浏览器抽象及仓库内维护的 CefGlue 源码。可执行自测试位于 `tests/Tarui.*.Tests`。仓库内的演示应用位于 `examples/demo/`（组合根 + 前端 + capabilities）。前端 pnpm 工作区位于 `web/`，React/Vite 应用在 `apps/Tarui.Web`，类型化桥接包在 `packages/api`，`examples/demo/web` 以 `workspace:*` 引用 `packages/api`。能力清单、工程脚本和文档分别位于 `capabilities/`、`eng/` 和 `docs/`。

## 文档结构（按受众划分）

`docs/` 按受众划分为四类；任何契约、配置或工作流的变化须同步更新对应文档。

### `docs/design/` —— 架构师 / 评审者

- [`README.md`](docs/design/README.md) —— 文档索引 + 关键设计不变量（10 条红线）
- [`architecture.md`](docs/design/architecture.md) —— 所有权边界、分层依赖、模块生命周期
- [`hosting.md`](docs/design/hosting.md) —— `Tarui.Hosting` 类型设计、配置键全表、Hosting vs Shell 分层
- [`ipc.md`](docs/design/ipc.md) —— Command / Event / Channel 三类 IPC、能力闸门、错误码、DTO 元数据
- [`plugin-system.md`](docs/design/plugin-system.md) —— 插件契约、`ITaruiPlugin` / `Add*Plugin()`、权限命名、Scope、模板与发布
- [`webview.md`](docs/design/webview.md) —— 浏览器栈分层、`Tarui.WebView.CefGlueNext` 适配、CefGlue 内置源码边界
- [`web-resource-mode.md`](docs/design/web-resource-mode.md) —— HTTP / Scheme 双模式、`TaruiAppOrigin`、Scheme 处理、CSP / SPA fallback
- [`alignment-plan.md`](docs/design/alignment-plan.md) —— 与 Tauri v2 桌面能力的逐项对齐进度表（权威状态表 §15）
- [`gap-analysis.md`](docs/design/gap-analysis.md) —— 与 Tauri v2 / Wails v3 的功能差距分析（P0/P1/P2 缺口）
- [`cli-workflow.md`](docs/design/cli-workflow.md) —— CLI / SDK / 插件双包分发设计稿（W0–W5 阶段记录）

### `docs/dev/` —— 框架贡献者

- [`README.md`](docs/dev/README.md) —— 文档索引 + 5 分钟跑起来
- [`environment.md`](docs/dev/environment.md) —— Windows / Linux / macOS 环境初始化、工具链、验证清单
- [`codebase-map.md`](docs/dev/codebase-map.md) —— 代码库地图、模块职责、依赖方向、关键不变量
- [`plugin-development.md`](docs/dev/plugin-development.md) —— 新增 / 修改插件的步骤、解剖、自测试
- [`ipc-protocol.md`](docs/dev/ipc-protocol.md) —— 修改 IPC 协议的三处同步规则、前端桥接
- [`webview-development.md`](docs/dev/webview-development.md) —— WebView / CefGlue 适配层开发、CefGlue 内置源码边界
- [`frontend-sdk.md`](docs/dev/frontend-sdk.md) —— `@lytree/api` 桥接包开发、模块命名约定
- [`testing.md`](docs/dev/testing.md) —— 控制台式自测试约定、基线门禁、架构测试
- [`ci-cd.md`](docs/dev/ci-cd.md) —— CI / Release 工作流、OIDC 发布、签名密钥
- [`local-workflow.md`](docs/dev/local-workflow.md) —— 本地提交流程、Conventional Commit、Agent 协作
- [`debugging.md`](docs/dev/debugging.md) —— 调试与诊断技巧、问题排查速查

### `docs/usage/` —— 应用开发者

- [`README.md`](docs/usage/README.md) —— 文档索引 + 三种使用姿势（模板 / 仓库内 Demo / NuGet 接入）
- [`quickstart.md`](docs/usage/quickstart.md) —— 5 分钟跑起 Demo / 模板脚手架
- [`scaffolding.md`](docs/usage/scaffolding.md) —— `tarui init` 与 `dotnet new tarui-app` 模板、`tarui.app.json` 清单
- [`configuration.md`](docs/usage/configuration.md) —— `appsettings.json` 与 `Tarui:Window:*` / `Tarui:Web:*` 配置键
- [`capabilities.md`](docs/usage/capabilities.md) —— `capabilities/*.json` 权限清单（IPC 闸门）、Scope allow/deny
- [`plugins.md`](docs/usage/plugins.md) —— 插件使用、Scope 配置、常见插件速查
- [`frontend-bridge.md`](docs/usage/frontend-bridge.md) —— `@lytree/api` 前端桥接、错误码、Channel
- [`webview-resource-mode.md`](docs/usage/webview-resource-mode.md) —— HTTP / Scheme 双模式应用开发者视角
- [`cli.md`](docs/usage/cli.md) —— `tarui` 命令行工具（init / dev / build / plugin）
- [`build-and-publish.md`](docs/usage/build-and-publish.md) —— zip / MSIX / `.app` / `.app.tar.gz` 安装包产物、签名
- [`troubleshooting.md`](docs/usage/troubleshooting.md) —— 常见问题排查速查

### `docs/adr/` —— 架构决策记录

- [`0001-macos-deeplink-appleevent-bridge.md`](docs/adr/0001-macos-deeplink-appleevent-bridge.md) —— macOS DeepLink 通过 `NSAppleEventManager` 桥接 `DeepLinkService`
- [`0002-macos-real-build-pipeline.md`](docs/adr/0002-macos-real-build-pipeline.md) —— macOS 真机构建管道（`.app` + `app-bundle` target）

## 构建、测试与本地开发

从仓库根目录运行 .NET 命令：

```powershell
./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64 # 首次安装 CEF
dotnet restore tarui.net.slnx --configfile NuGet.Config
dotnet build tarui.net.slnx --no-restore
dotnet run --project tests/Tarui.Ipc.Tests --no-build
dotnet run --project tests/Tarui.Architecture.Tests --no-build
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj
```

提交较大改动前，应运行全部 `tests/Tarui.*.Tests` 可执行项目。前端命令从 `web/` 运行：

```powershell
pnpm install --frozen-lockfile
pnpm dev
pnpm lint
pnpm build
```

## 编码风格与命名约定

C# 面向 .NET 10，启用可空引用、隐式 using、最新语言版本和推荐分析规则，并将警告视为错误。遵循现有四空格缩进、文件范围命名空间、类型与成员使用 `PascalCase`、私有字段使用 `_camelCase`、异步方法以 `Async` 结尾。TypeScript 使用两空格、ES 模块、React 函数组件和 `camelCase`，并通过 Oxlint 检查。`src/webview/cefglue` 是内置第三方源码，应尽量减少无关改动。

## 测试指南

测试是控制台式自测试项目，而非 xUnit/NUnit 套件。将测试加入对应项目的 `Program.cs`，使用行为描述型名称，例如 `DeniesCommandsOutsideCapability`，并提供明确的失败信息。仓库暂无覆盖率门槛，但新行为和回归修复必须有针对性测试。依赖关系或分层变更后必须运行 `Tarui.Architecture.Tests`。

## 提交与拉取请求

历史提交采用 Conventional Commit，例如 `feat: implement ...`、`refactor: compose ...`、`chore: initialize ...`。提交应聚焦单一目的，摘要使用祈使语气。PR 应说明行为和架构影响、列出验证命令、关联相关 Issue；可见的 Web 或桌面 UI 变化需附截图。契约、配置或工作流变化时同步更新 `README.md` 或 `docs/`。

## 架构与 Agent 约束

禁止引入运行时反射、程序集扫描、动态插件加载或 JSON 反射回退。插件必须显式注册；线协议 DTO 必须使用源码生成元数据；新增命令时同步更新处理器、权限和对应能力清单。Agent 辅助任务应主动拆分独立子任务，优先使用 Luna，其次使用 Terra，并在集成前审查所有委派结果。
