# tarui.net

`tarui.net` 将 Avalonia 原生壳、React/TypeScript 业务前端与独立的 `CefGlue.Next.Avalonia` 浏览器组件整合在一起。

## 架构

- Avalonia 负责原生窗口外壳与平台组件。
- `CefGlue.Next.Avalonia` 是 Tarui 侧接入内置 CefGlue 实现与 Avalonia 浏览器控件的唯一入口。
- `Tarui.WebView.CefGlueNext` 把该组件适配到 Tarui 的策略、IPC 与事件体系;Shell 与 Hosting 不引用任何 CefGlue 类型。
- IPC 沿用 Tauri 形态:Command、Event、Channel、Capability。
- 禁止运行时反射、程序集扫描、动态插件加载以及 JSON 反射回退。
- Hosting 沿用 ASP.NET Core 模式:`TaruiHost.CreateApplicationBuilder` 在 `Microsoft.Extensions.Hosting` 之上,通过 `AddTaruiShell()` 与 `Add*Plugin()` DI 扩展组合壳层。
- 插件以 ProjectReference 形式在组合根通过 `AddPlugin<T>()` / `Add*Plugin()` 显式注册 —— 编译期注册,不做程序集扫描。

桌面能力的扩展与 Tauri v2 对齐进度见
[`docs/tauri-desktop-alignment-plan.md`](docs/tauri-desktop-alignment-plan.md)。

**已落地的近期能力**
- **Channel 端到端流式 IPC**：`Channel` 令牌下沉到原生命令，`SendAsync` 逐帧回传，背压由
  `WebviewSession` 的 `ExecuteScriptAsync` await 天然提供。
- **fs 大文件 + 目录监听**：`plugin:fs|read-file-stream`（流式读，突破 8 MiB 单次上限）与
  `write-begin|write-chunk|write-commit|write-cancel`（分片写 + 原子提交 + 窗口级清理）；`plugin:fs|watch|unwatch`
  目录监听并以 `fs://watch-change` 定向事件投递。
- **HTTP 客户端**：`plugin:http|fetch`（`src/plugins/Tarui.Plugins.Http`）——URL 作用域默认拒绝、
  重定向逐跳复检、内联与流式响应双模式，另有 `plugin:http|upload`（multipart/form-data 上传，同安全模型）；
  前端经 `@lytree/api/http` 调用。
- **Shell 子进程**：`plugin:shell|spawn|stdin|kill`（`src/plugins/Tarui.Plugins.Shell`）——程序白名单作用域
  默认拒绝、stdout/stderr 经 Channel 流式回传、退出码 terminated 帧、进程树终止；前端经 `@lytree/api/shell`。
- **上下文菜单 + Dialog ask**：`plugin:menu|show-context-menu` 任意坐标弹出（复用声明式 items + `menu://item-clicked`
  点击路由）；`plugin:dialog|ask` Yes/No 三态询问（可选显式取消）；前端经 `@lytree/api/menu`、`@lytree/api/dialog`。
- **Updater apply + 打包分发**：`plugin:updater|apply` 对已校验暂存的 MSIX 执行安装（Windows `Add-AppxPackage`）
  并广播 apply 状态事件；`tarui build` 产出 zip / 自研 MSIX 打包器 / 签名 `latest.json`；重启由前端经
  `@lytree/api/updater` 衔接 `process.relaunch`。
- **平台能力矩阵 + 跨平台自启**：`core:platform|capabilities` 暴露 notification/global-shortcut/autostart/deep-link
  的真实可用性，前端据此禁用不可用 UI；Autostart 覆盖三平台（Windows registry / macOS LaunchAgents / Linux `.desktop`），
  前端经 `@lytree/api/platform`、`@lytree/api/autostart` 调用。
- **WebView 网络配置**：CEF 运行时支持自定义 User-Agent 与代理，经 `TARUI_WEB_USER_AGENT` / `TARUI_WEB_PROXY_SERVER`
  在初始化期配置（CEF 不支持运行时修改）。

逐项能力与安装/打包细节见 [docs/wails-tauri-gap-analysis.md](docs/wails-tauri-gap-analysis.md) 与
[examples/demo](examples/demo)。

## 仓库目录

```text
src/
  core/                    无反射的契约与 IPC 运行时
  desktop/
    Tarui.Hosting/          ASP.NET Core 风格主机:builder、DI、配置、日志、host 生命周期
    Tarui.Shell/            声明式壳与窗口组合
  generators/              编译期 Roslyn 生成器
  plugins/                 显式注册的原语能力插件
  webview/
    cefglue/                内置 CefGlue 托管源码项目
    CefGlue.Next.Avalonia/  独立 Avalonia 浏览器组件与运行时生命周期
    Tarui.WebView.*         Tarui 浏览器契约与组件适配层
examples/
  demo/                    仓库内演示应用(组合根 + 前端),基于运行时构建
web/
  apps/Tarui.Web/          React 业务应用
  packages/api/            @lytree/api 桥接包
tests/                     可执行与集成自测试
capabilities/              窗口/WebView 权限清单
runtime/cef/               本地安装的 CEF 原生发行版
docs/                      架构与实现说明
```

## 构建

```powershell
./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64

dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln --no-restore

dotnet run --project tests/Tarui.Http.Tests --no-build
dotnet run --project tests/Tarui.ShellPlugin.Tests --no-build
dotnet run --project tests/Tarui.Ipc.Tests --no-build
dotnet run --project tests/Tarui.Shell.Tests --no-build
dotnet run --project tests/Tarui.Plugins.Tests --no-build
dotnet run --project tests/Tarui.Hosting.Tests --no-build
dotnet run --project tests/Tarui.Architecture.Tests --no-build

dotnet pack tarui.net.sln -c Release -o artifacts/nuget
dotnet run --project tests/Tarui.Architecture.Tests --no-build -- --require-package --package artifacts/nuget/CefGlue.Next.Avalonia.0.1.0.nupkg

cd web
pnpm install --frozen-lockfile
pnpm lint
pnpm build
```

Web 工作区使用 pnpm 11。`web/pnpm-workspace.yaml` 定义 `apps/*` 与 `packages/*` 成员,共享的 `web/pnpm-lock.yaml` 保证依赖解析可复现。应用通过 `workspace:*` 依赖规约消费 `@lytree/api`。所有 Web 命令须在 `web/` 目录下执行。

CefGlue 托管程序集与 CEF 原生运行时资产均不通过 NuGet 还原。原生运行时安装器会下载官方 CEF 最小发行版并校验其发布的 SHA-1。Avalonia 自身仍是普通的框架包依赖。

为了 CI 与本地可复现安装,请使用 `web/package.json` 中锁定的 pnpm 11 工具链并从锁文件安装:

```powershell
cd web
pnpm install --frozen-lockfile
```

## 托管与运行时配置

`examples/demo`(`Demo` 应用)是仓库内的组合根。它通过 Tarui.Hosting builder 启动,后者封装 `Microsoft.Extensions.Hosting` 并暴露熟悉的 `Configuration` / `Logging` / `Services` / `Window` 成员。CEF 子进程派发由 `CefGlue.Next.Avalonia` 负责:

```csharp
using CefGlue.Next.Avalonia;
using Tarui.Hosting;
using Tarui.Shell;

if (CefGlueNextAvaloniaRuntime.RunSubProcess(args))
{
    return;
}

var builder = TaruiHost.CreateApplicationBuilder(args);

builder.Services
    .AddTaruiShell()
    .AddCefGlueWebView()
    .AddCorePlugin()
    .AddWindowPlugin()
    .AddEventPlugin()
    .AddDialogPlugin()
    .AddSystemPlugin();

builder.Window.Configure(window =>
{
    window.Title = "tarui.net";
    window.Width = 1280;
    window.Height = 820;
});

try
{
    builder.Build().Run();
}
finally
{
    CefGlueNextAvaloniaRuntime.Shutdown();
}
```

## WebView 组件边界

浏览器栈刻意划分为四层:

| 层 | 职责 | 可引用的依赖 |
| --- | --- | --- |
| `Tarui.WebView.Abstractions` | UI 中立的导航、脚本、下载、文件拖放与拖拽区域契约 | 无 Avalonia,无 CefGlue |
| `Tarui.WebView.Avalonia` | 承载 Avalonia `Control` 的契约 | Avalonia + Tarui WebView 契约 |
| `CefGlue.Next.Avalonia` | 直接的 Avalonia 浏览器控件、CefGlue handler、运行时与原生浏览器生命周期 | Avalonia + 内置 CefGlue |
| `Tarui.WebView.CefGlueNext` | Tarui 配置、IPC、Capability 策略与事件翻译 | Tarui 契约 + `CefGlue.Next.Avalonia` |

直接使用 Avalonia 的应用:安装 `CefGlue.Next.Avalonia`,在 host 启动前调用 `CefGlueNextAvaloniaRuntime.RunSubProcess(args)`,初始化一份运行时配置,嵌入 `CefGlueNextAvaloniaWebView`,并在退出 Avalonia 消息循环前 await 所有 WebView 的关闭。随后应用停止并释放 Host,在 `Program` 的 `finally` 块中调用 `CefGlueNextAvaloniaRuntime.Shutdown()`。Tarui 应用通常使用 `Tarui.WebView.CefGlueNext`,以便 Shell 施加窗口 capability 与 IPC 策略。

运行时设置从可执行文件同目录的 `appsettings.json`、环境变量、命令行加载:

- `Tarui:Window:*` —— 主窗口标题、尺寸、最小尺寸、居中与 URL。合并优先级为:默认值 < 配置 < `builder.Window` 代码配置。
- `Tarui:Web:*` —— WebView 资源模式参数(`Mode`、`Url`、`Root`、`Scheme`、`Host`、`SpaFallback`、`Csp`、`MaxAssetBytes`)。下方列出的 `TARUI_WEB_*` 环境变量仍作为兜底被支持。
- `Logging:LogLevel:*` —— 标准 `Microsoft.Extensions.Logging` 配置。

Demo 应用把 capability 清单放在 `examples/demo/capabilities/`;构建把 `capabilities/*.json` 拷贝到与 `appsettings.json` 同级的应用输出,Host 从 `AppContext.BaseDirectory` 解析两者。完整设计与配置键全表见 [`docs/hosting.md`](docs/hosting.md)。

## Web 资源模式

HTTP 开发模式:

```powershell
$env:TARUI_WEB_MODE = "http"
$env:TARUI_WEB_URL = "http://127.0.0.1:5173"
cd web
pnpm dev
```

无 HTTP 服务器的本地 Scheme 模式:

```powershell
cd web
pnpm build
cd ..
$env:TARUI_WEB_MODE = "scheme"
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj
```

Scheme 模式服务 `tarui://localhost/index.html`,构建会把 Web `dist` 文件复制到应用输出。可用 `Tarui:Web:*` 配置键或等价环境变量覆盖默认值:

- `TARUI_WEB_ROOT`:包含 `index.html` 的静态资源目录。
- `TARUI_WEB_SCHEME` / `TARUI_WEB_HOST`:自定义 origin。
- `TARUI_WEB_SPA_FALLBACK=false`:关闭主帧 SPA 回退。
- `TARUI_WEB_CSP`:覆盖生产环境 Content-Security-Policy。
- `TARUI_WEB_MAX_ASSET_BYTES`:资源体积上限,默认 64 MiB。

未显式指定模式时,若设置了 `TARUI_WEB_URL` 则选择 HTTP;否则若有打包好的 Web 目录则选择 Scheme,仅在没有打包资源时才回退到本地开发 HTTP URL。

HTTP 与自定义 app scheme 可以共存:当配置了 content root(`TARUI_WEB_ROOT` 或打包资产),HTTP 模式也会注册无端口的自定义 scheme(如 `tarui://localhost/`),从而允许窗口与 WebView 同时加载远程 HTTP 内容与本地资产。默认导航策略放行所有应用 origin —— HTTP 起始 origin、自定义 scheme origin 与本地 dev server —— `TaruiAppOrigin.AllowedSchemes` / `SchemeOrigin` 暴露可接受的 scheme 以便校验。

## Tarui CLI

`src/tarui-cli`(`Tarui.Cli`)是零依赖的编排器,读取 `tarui.app.json` 清单(默认当前目录的 `./tarui.app.json`;通过 `--config <path>` 指定仓库内 `examples/demo/tarui.app.json`)并驱动前端/后端流水线。以 `dotnet tool` 形式发布,工具名 `tarui`:

```powershell
dotnet tool install -global Tarui.Cli

tarui init       # 从 tarui-app 模板脚手架新应用(--local <repo> 用于仓库内开发)
tarui dev        # 开发服务器(build.beforeDevCommand) + dotnet watch,Ctrl+C 同步拆除
tarui build      # 前端构建、自包含发布、zip/msix 安装器 + latest.json
tarui plugin init <name>   # 脚手架插件骨架(permissions/、guest-js/、tests/;--local <repo>)
tarui plugin pack          # 插件预检:布局/权限/版本一致性、自测试、双包打包
tarui info       # 环境 / 工具链 / 清单诊断
tarui --help     # 完整命令面
```

`tarui dev` 在 `build.frontend` 内启动 `build.beforeDevCommand`,等待 `build.devUrl` 可达,然后以 `TARUI_WEB_MODE=http` 与 `TARUI_WEB_URL=<devUrl>` 启动桌面项目。`tarui build` 会运行 `build.beforeBuildCommand`,校验 `build.frontendDist`,为当前 RID 自包含发布桌面项目,然后产出配置的 `bundle.targets`:可移植 `zip` + MSIX(`--bundle msix` 或 `bundle.targets: ["zip","msix"]`),以及带 SHA-256 的升级器蓝图 `dist/latest.json`。MSIX 由托管实现的 `MsixPacker` 构建(OPC ZIP + `AppxManifest.xml` + SHA-256 `AppxBlockMap.xml`,不依赖 `makeappx`);若配置了 `bundle.msix.certificate.{path,password,timeStamperUrl}`,将通过 `signtool.exe` 做 Authenticode 签名,否则产未签名包。`build` 还会把所有引用插件的 `permissions/<plugin>/schema.json` 合并进 `schemas/permissions.schema.json`(仅作校验辅助,运行时仍以 `capabilities/*.json` 为唯一授权源)。`tarui plugin init` 生成带权限描述符、强类型 guest-js 桥接和控制台自测试的插件;`tarui plugin pack` 校验布局、权限/版本一致性,运行自测试,并同时打包 NuGet 后端(含 `permissions/`)与 npm 前端。在应用目录下执行(也可传 `--config <path>` 指向仓库内 `examples/demo/tarui.app.json`);清单 schema 与分阶段实施见 `docs/dev-workflow-design.md`(W3 应用模板 / `tarui init` 已完成,W4 插件工作流已完成,W5 安装器已完成)。

## Demo 应用

[`examples/demo`](examples/demo) 是仓库内示例应用,把桌面宿主、`core:window|*`、`core:event|emit`、`plugin:fs|*`、`plugin:store|*` 插件以及 `@lytree/api` 前端桥接端到端接起来。它通过 `ProjectReference` 直接构建 `src/` 树(不依赖已发布包),因此永远跟踪当前源码。

```powershell
cd examples/demo/Demo.Desktop
dotnet run --project Demo.Desktop.csproj
```

React 前端(`examples/demo/web`)演示窗口 + IPC 状态控制、路由事件以及隔离的 `appData` store/fs 访问。其 `capabilities/main.json` 仅授予 Demo 用到的权限。

## CI 与发布

GitHub Actions 自动化集成与发布门禁(设计稿 §10):

- `.github/workflows/ci.yml` —— PR / 分支门禁:`dotnet build` 0 警告、`CefGlue.Next.Avalonia` 的包/nuspec 校验、外部 NuGet 消费者 restore/build 冒烟、所有自测试、`Tarui.Architecture.Tests`、版本一致性(`Directory.Build.props` == `@lytree/api`)、`pnpm lint` + `pnpm build`。
- `.github/workflows/release.yml` —— tag `tarui-v<version>`(或手动触发):在推送 NuGet 包之前执行同样的组件包与外部消费者门禁,发布 `@lytree/api`,在 Windows 上构建 `zip;msix` 安装器(可选 Authenticode),并创建带产物的 GitHub Release。

发布密钥保存在 GitHub `release` 环境。NuGet 发布使用 [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)(OIDC,无需长期 API key):在 nuget.org 上 allowlist `release` 环境与 `release.yml` 工作流文件名,然后添加 `NUGET_USER` 环境密钥(nuget.org profile 名,不是邮箱)。`@lytree/api` 通过 [provenance](https://docs.npmjs.com/generating-provenance-statements)(OIDC)发布到 npm —— 在 npmjs.com 上为该仓库添加 `NPM_USER` 关联的 trusted-publisher 条目,无需 `NPM_TOKEN`。可选:`NUGET_SOURCE`。MSIX 签名可选:`WINDOWS_CERT_BASE64`、`WINDOWS_CERT_PUBLISHER`、`WINDOWS_CERT_PASSWORD`、`WINDOWS_CERT_TIMESTAMP`;无证书时 MSIX 以未签名形式产出。

## 原生能力面

`AddTaruiShell()` 从显式注册的插件组合壳。每个命令都会根据调用方窗口的能力文件(`capabilities/main.json`)做权限校验:

| 插件 | 命令 | 能力 |
| --- | --- | --- |
| Core | `core:app|get-info` | 壳握手:product、version、capabilities。 |
| Window | `core:window|*`(24 条) | 创建/关闭/最小化/最大化/隐藏/显示/聚焦/居中、标题、尺寸、位置、最小/最大尺寸、置顶、可缩放、装饰、全屏、状态、监视器、列表。 |
| Event | `core:event|emit` | 从 Web 侧发出路由或广播事件。 |
| Dialog | `plugin:dialog|open`、`plugin:dialog|save`、`plugin:dialog|message`、`plugin:dialog|confirm` | 绑定到请求窗口的原生文件/目录选择器、消息框与确认框。 |
| System | `core:path|resolve`、`core:os|info`、`core:process|exit`、`core:process|relaunch`、`core:shell|open`、`core:clipboard|read-text`、`core:clipboard|write-text` | 路径解析(含越界保护)、OS 信息、进程生命周期、OS 默认处理器、剪贴板文本。 |

Shell 把窗口生命周期事件路由到所属 Webview(`window://moved`、`window://resized`、`window://focus-changed`、`window://close-requested`),并向所有窗口广播 `window://destroyed` 与 `shell://theme-changed`。关闭是协作式的:标题栏的关闭请求被作为事件投递,Web 端通过调用 `core:window|close` 来确认。

## 前端桥接

`web/packages/api`(`@lytree/api`)为每个插件契约提供强类型模块:`ipc`、`app`、`window`、`event`、`dialog`、`os`、`path`、`process`、`shell`、`clipboard`。`Window.getCurrent()` 返回一个句柄,无 label 的调用指向承载当前 Webview 的窗口;`Window.getByLabel` / `Window.create` 用于寻址其它窗口。Barrel 导出把两个 `open` helper 重命名为 `openDialog` 与 `openExternal`;子路径导出(如 `@lytree/api/window`)保留 Tauri 风格的简短名。

## CefGlue 移植

源码移植基于上游 commit `e3389315dad795374be1a1e52c42d4e49cb6fe7b`,CEF `150.0.11`,目标 Avalonia `12.1.1`。已移除基于反射的 ObjectBinding、泛型 JavaScript 求值、ReactiveUI 与 System.Reactive。Tarui IPC 通过固定的 `window.invokeCSharpAction` CEF 进程消息桥进入。

当前移植通过 `CefGlue.Next.Avalonia` 支持原生窗口渲染。OSR 与对应的 Avalonia 11 拖放层被刻意排除。托管组件包内嵌所有必需的 Xilium CefGlue 程序集,且刻意不依赖 Xilium 包;原生 CEF 文件仍由 `eng/cef/install-runtime.ps1` 或未来的 RID runtime 包安装。
