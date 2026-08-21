# Repository Guidelines

## 项目结构与模块组织

主解决方案为 `tarui.net.sln`。生产代码位于 `src/`：`core/` 存放契约与无反射 IPC，`desktop/` 存放 Hosting、Shell 和应用组合根，`plugins/` 存放原生能力插件，`generators/` 存放 Roslyn 生成器，`webview/` 存放浏览器抽象及仓库内维护的 CefGlue 源码。可执行自测试位于 `tests/Tarui.*.Tests`。前端 pnpm 工作区位于 `web/`，React/Vite 应用在 `apps/Tarui.Web`，类型化桥接包在 `packages/api`。能力清单、工程脚本和设计文档分别位于 `capabilities/`、`eng/` 和 `docs/`。

## 构建、测试与本地开发

从仓库根目录运行 .NET 命令：

```powershell
./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64 # 首次安装 CEF
dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln --no-restore
dotnet run --project tests/Tarui.Ipc.Tests --no-build
dotnet run --project tests/Tarui.Architecture.Tests --no-build
dotnet run --project src/desktop/Tarui.App/Tarui.App.csproj
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
