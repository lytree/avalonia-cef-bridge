# tarui.net

`.NET` 生态下的 Tauri v2 同构桌面框架:把 Avalonia 原生壳、React/TypeScript 业务前端与 `Tarui.WebView.CefGlueNext`(内置 CEF 150.x)整合为一套 IPC + Capability + Channel 的现代桌面开发体验。

## 文档导航

文档按受众划分为三类,请按角色选择入口:

| 角色 | 入口 | 你将学到 |
| --- | --- | --- |
| **应用开发者** —— 用 tarui.net 构建一个跨平台桌面应用 | [`docs/usage/`](docs/usage/README.md) | 脚手架、配置、Capability、前端桥接、CLI、构建与发布 |
| **框架贡献者** —— 修改 Shell/Hosting/Ipc、新增/调整插件、维护架构门禁 | [`docs/dev/`](docs/dev/README.md) | 代码库地图、设计不变量、测试、CI、本地提交流程 |
| **架构师 / 评审者** —— 理解所有权边界、IPC 模型、能力授权、生命周期 | [`docs/design/`](docs/design/README.md) | 架构总览、Hosting、IPC/Capability、插件系统、ADRs |

补充参考:

- [`docs/design/alignment-plan.md`](docs/design/alignment-plan.md) —— 与 Tauri v2 桌面能力的逐项对齐进度表
- [`docs/design/gap-analysis.md`](docs/design/gap-analysis.md) —— 与 Tauri v2 / Wails v3 的功能差距分析
- [`docs/design/cli-workflow.md`](docs/design/cli-workflow.md) —— CLI / SDK / 插件分发设计稿
- [`docs/adr/`](docs/adr/) —— 架构决策记录(macOS DeepLink 桥、macOS 真机构建管道 等)

---

## 架构速览

- **壳**:Avalonia 12.1.1 原生窗口、标题栏、对话框、平台能力。
- **浏览器**:`Tarui.WebView.CefGlueNext` 自带 CEF 150.x 渲染进程(仓库内嵌管理端 CefGlue 源码)。
- **WebView 适配层**:`Tarui.WebView.CefGlueNext` 把浏览器组件接入 Tarui 的 IPC、事件、资源策略;Shell/Hosting 不引用 CefGlue 类型。
- **业务前端**:React + TypeScript + Vite,通过 `@lytree/api` 与宿主通信。
- **IPC**:Command(请求/响应)、Event(低频通知)、Channel(命令关联的有序进度)、Capability(命令/事件白名单)。
- **架构约束**:禁止运行时反射、程序集扫描、动态插件加载、JSON 反射回退。所有插件 **编译期显式注册**。

## 仓库目录

```text
src/
  core/                    无反射的契约与 IPC 运行时
  desktop/
    Tarui.Hosting/         ASP.NET Core 风格主机(builder、DI、配置、日志、host 生命周期)
    Tarui.Shell/           声明式壳与窗口组合
    Tarui.SingleInstance/  单实例守卫与 IPC 转发
  generators/              编译期 Roslyn 生成器
  plugins/                 显式注册的原语能力插件(20+ 个)
  webview/
    cefglue/               内置 CefGlue 托管源码项目
    Tarui.WebView.*        Tarui 浏览器契约 + 浏览器组件 + 运行时生命周期
  tarui-cli/               `tarui` 命令行工具(零第三方依赖的纯编排器)
  templates/               `dotnet new tarui-app` 模板包
examples/demo/             仓库内组合根示例(组合根 + 前端 + capabilities)
web/                       前端 pnpm 工作区(apps/Tarui.Web + packages/api)
tests/                     控制台式自测试(*.Tests,21+ 套)
capabilities/              窗口/WebView 权限清单(Demo 与编辑器)
runtime/cef/               本地安装的 CEF 原生发行版
schemas/                   tarui.app.json 与 capability 的 JSON Schema
docs/                      本仓库文档(design / dev / usage / adr)
eng/                       工程脚本(CEF 安装、test-all 调度)
.github/workflows/         CI / Release 工作流
```

## 快速上手(应用开发者)

```powershell
# 1. 安装 CLI
dotnet tool install -g Tarui.Cli

# 2. 新建应用
tarui init my-app
cd my-app

# 3. 开发(Vite HMR + dotnet watch)
tarui dev

# 4. 生产构建(产物到 dist/)
tarui build --bundle zip,msix
```

详细见 [`docs/usage/quickstart.md`](docs/usage/quickstart.md)。

## 快速上手(框架贡献者)

```powershell
# 仓库根
git clone <repo> && cd tarui.net

# 装 CEF 原生运行时(首次)
./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64

# 还原 + 构建
dotnet restore tarui.net.slnx --configfile NuGet.Config
dotnet build tarui.net.slnx --no-restore

# 跑全部自测试 + 架构门禁
./eng/test-all.ps1 -BaselineCount 21
dotnet run --project tests/Tarui.Architecture.Tests --no-build
```

详细见 [`docs/dev/environment.md`](docs/dev/environment.md) 与 [`docs/dev/local-workflow.md`](docs/dev/local-workflow.md)。

## 已落地的近期能力

- **Channel 端到端流式 IPC** —— 令牌下沉到原生命令,`SendAsync` 逐帧回传,背压由 `WebviewSession.ExecuteScriptAsync` await 天然提供;解锁 fs 大文件流式、HTTP 流式与 Shell 子进程 stdio。
- **fs 大文件 + 目录监听** —— `plugin:fs|read-file-stream` 突破 8 MiB 单次上限;`write-begin|chunk|commit|cancel` 分片写 + 原子提交;`watch|unwatch` 目录监听以 `fs://watch-change` 定向事件投递。
- **HTTP 客户端** —— `plugin:http|fetch`(URL scope 默认拒绝、重定向逐跳复检、内联/流式响应)+ `plugin:http|upload`(multipart/form-data)。
- **Shell 子进程** —— `plugin:shell|spawn|stdin|kill`(程序白名单作用域默认拒绝、stdout/stderr 经 Channel 流式回传、退出码 terminated 帧、进程树终止)。
- **上下文菜单 + Dialog ask** —— `plugin:menu|show-context-menu` 任意坐标弹出;`plugin:dialog|ask` Yes/No 三态询问。
- **Updater check/download/apply** —— ECDSA(P-384/SHA-384)签名验证 + 逐文件 SHA-256 核验 + 受控 staging;Windows MSIX 安装器;macOS / Linux `NoOpUpdateApplier` 显式声明不支持;apply 默认 capability 不授权。
- **DeepLink** —— Windows `HKCU\Software\Classes\<scheme>` 注册 + argv/SingleInstance 转发;Linux `.desktop`(x-scheme-handler)+ cold/warm argv;macOS `NSAppleEventManager` `kAEGetURL` 桥 + 2s 去重窗口,真机验收待执行。
- **单实例 + 窗口状态** —— Mutex / Unix socket 抢占 + Named Pipe / socket 转发;`plugin:window-state|save|restore|clear` 显示器拟合。
- **平台能力矩阵 + 跨平台自启** —— `core:platform|capabilities` 暴露真实可用性;Autostart 三平台(Windows registry / macOS LaunchAgents / Linux `.desktop`)。
- **Cookie / DevTools / Clipboard / CLI** —— `plugin:cookie|list|set|remove|flush`(CEF 全局存储,无宿主时降级);`plugin:webview|devtools`(权限门控);剪贴板扩展(HTML + PNG 字节);`core:cli|parse` 结构化参数解析。
- **窗口增强** —— `set-icon` / `set-theme` / `Transparent` / `Parent`+`Modal` / `deny-close` 可取消关闭钩子。
- **WebView 深度集成** —— 真实 windowed CEF:`window://file-drop-*` 定向事件、`webview://download-requested/navigation-requested` 策略化决策、`DraggableRegion` 命中与 NoDrag 覆盖;`webview://render-process-gone` 渲染进程崩溃事件;`plugin:webview|eval`/`eval-with-callback` 脚本回执。
- **Websocket / Persisted Scope / Positioner** —— `plugin:websocket|connect/send/close`(`ClientWebSocket` + Channel 帧推送);`plugin:persisted-scope|scope-allow/deny/reset` + `scope://changed` 事件 + `appData/scope.json` 原子持久化;`plugin:positioner|set-position`(8 屏幕锚位 + 6 Tray* 锚位)。
- **打包与 CI/CD** —— `tarui build` 产出 `zip` / 自研 MSIX 打包器 / 签名 `latest.json`;新增 `app-bundle` target 走 `MacOsBundleBuilder` + `InfoPlistBuilder` 输出 `<name>.app` 与 `<name>.app.tar.gz`;macOS 真机构建管道见 [docs/adr/0002](docs/adr/0002-macos-real-build-pipeline.md)。

权威状态表见 [`docs/design/alignment-plan.md §15`](docs/design/alignment-plan.md);缺口分析见 [`docs/design/gap-analysis.md`](docs/design/gap-analysis.md)。

## 托管与运行时配置

`examples/demo`(`Demo` 应用)是仓库内的组合根。它通过 Tarui.Hosting builder 启动,后者封装 `Microsoft.Extensions.Hosting` 并暴露熟悉的 `Configuration` / `Logging` / `Services` / `Window` 成员:

```csharp
using Tarui.Hosting;
using Tarui.Plugins.Core;
using Tarui.Plugins.Window;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.System;
using Tarui.Shell;
using Tarui.WebView.CefGlueNext;

if (CefGlueNextAvaloniaRuntime.RunSubProcess(args)) return; // CEF 子进程短路

var builder = TaruiHost.CreateApplicationBuilder(args);
builder.Services
    .AddTaruiShell()
    .AddCefGlueWebView()
    .AddCorePlugin()
    .AddWindowPlugin()
    .AddDialogPlugin()
    .AddSystemPlugin();

builder.Window.Configure(w => { w.Title = "tarui.net"; w.Width = 1280; w.Height = 820; });
try { builder.Build().Run(); }
finally { CefGlueNextAvaloniaRuntime.Shutdown(); }
```

完整设计与配置键全表见 [`docs/design/hosting.md`](docs/design/hosting.md) 与 [`docs/usage/configuration.md`](docs/usage/configuration.md)。

## CI 与发布

GitHub Actions 自动化集成与发布门禁:

- `.github/workflows/ci.yml` —— PR / 分支门禁:`dotnet build` 0 警告、`Tarui.WebView.CefGlueNext` 包/nuspec 校验、外部 NuGet 消费者 restore/build 冒烟、所有自测试、`Tarui.Architecture.Tests`、版本一致性(`Directory.Build.props` == `@lytree/api`)、`pnpm lint` + `pnpm build`。
- `.github/workflows/ci-macos.yml` —— macOS 真机门禁:`tarui build --bundle app-bundle` 产物结构校验 + `Tarui.DeepLink.Tests` / `Tarui.Ipc.Tests` / `Tarui.Shell.Tests` 在 Apple Silicon .NET 10 上不退化。
- `.github/workflows/release.yml` —— tag `tarui-v<version>`(或手动触发):在推送 NuGet 包之前执行同样的组件包与外部消费者门禁,发布 `@lytree/api`,在 Windows (`zip;msix`)、macOS (osx-arm64 `.app.tar.gz`)、Linux (linux-x64 self-contained `zip`) 三个 runner 上并行构建,所有产物由 `softprops/action-gh-release` 上传到带产物的 GitHub Release。

发布密钥保存在 GitHub `release` 环境。NuGet 发布使用 [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)(OIDC,无需长期 API key);`@lytree/api` 通过 [provenance](https://docs.npmjs.com/generating-provenance-statements)(OIDC)发布到 npm —— **无需长期 API key**。

## CefGlue 移植

源码移植基于上游 commit `e3389315dad795374be1a1e52c42d4e49cb6fe7b`,CEF `150.0.11`,目标 Avalonia `12.1.1`。已移除基于反射的 ObjectBinding、泛型 JavaScript 求值、ReactiveUI 与 System.Reactive。Tarui IPC 通过固定的 `window.invokeCSharpAction` CEF 进程消息桥进入。

当前移植通过 `Tarui.WebView.CefGlueNext` 支持原生窗口渲染。OSR 与对应的 Avalonia 11 拖放层被刻意排除。托管组件包内嵌所有必需的 Xilium CefGlue 程序集,且刻意不依赖 Xilium 包;原生 CEF 文件由 `eng/cef/install-runtime.ps1` 或未来的 RID runtime 包安装。
