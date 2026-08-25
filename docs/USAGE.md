# tarui.net 使用文档

> 面向 **应用开发者**:用 tarui.net 构建一个跨平台桌面应用应当如何接线、跑起来、打包发布。
>
> 配套文档:[`DEV.md`](DEV.md)(框架贡献者指南)、[`ENVIRONMENT.md`](ENVIRONMENT.md)(环境初始化)、[`architecture.md`](architecture.md)(架构总览)、[`hosting.md`](hosting.md)(托管层详解)。

---

## 1. tarui.net 是什么

`tarui.net` 是 .NET 生态下对齐 Tauri v2 工作流的桌面开发框架:

- **壳**:Avalonia 12.1.1 原生窗口、标题栏、对话框、平台能力。
- **浏览器**:`CefGlue.Next.Avalonia` 自带 CEF 150.x 渲染进程(仓库内嵌管理端 CefGlue 源码)。
- **WebView 适配层**:`Tarui.WebView.CefGlueNext` 把浏览器组件接入 Tarui 的 IPC、事件、资源策略。
- **业务前端**:React + TypeScript + Vite,通过 `@lytree/api` 与宿主通信。
- **IPC 模型**:Command(请求/响应)、Event(低频通知)、Channel(命令关联的有序进度)、Capability(命令/事件白名单)。
- **架构约束**:禁止运行时反射、程序集扫描、动态插件加载、JSON 反射回退。所有插件 **编译期显式注册**。

整体形态与 Tauri v2 对齐:Rust 侧 → .NET 侧、`@tauri-apps/api` → `@lytree/api`、`tauri.conf.json` → `tarui.app.json`、能力清单(`permissions/*.toml`)→ `capabilities/*.json`。

---

## 2. 三种使用姿势

| 姿势 | 入口 | 适合 |
| --- | --- | --- |
| **模板脚手架** | `tarui init my-app` 或 `dotnet new tarui-app -n MyApp -o MyApp` | 新建独立应用、典型用法 |
| **仓库内 Demo** | `examples/demo/Demo.Desktop/Demo.Desktop.csproj` | 研究完整接线、调试框架能力 |
| **直接接入 NuGet** | 引入 `Tarui.Hosting`、`Tarui.Shell`、`Tarui.WebView.CefGlueNext` 等 NuGet 包 | 在既有 .NET 项目中嵌入 Tarui 壳 |

三种姿势都遵循同一个组合根模式(ASP.NET Core 风格):

```csharp
using Tarui.Hosting;
using Tarui.Plugins.Core;
using Tarui.Plugins.Window;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.System;
using Tarui.Shell;
using Tarui.WebView.CefGlueNext;
using CefGlue.Next.Avalonia;

if (CefGlueNextAvaloniaRuntime.RunSubProcess(args)) return; // CEF 子进程短路

var builder = TaruiHost.CreateApplicationBuilder(args);
builder.Services
    .AddTaruiShell()
    .AddCefGlueWebView()
    .AddCorePlugin()
    .AddWindowPlugin()
    .AddDialogPlugin()
    .AddSystemPlugin();

builder.Window.Configure(w => { w.Title = "my-app"; w.Width = 1280; w.Height = 820; });
try { builder.Build().Run(); }
finally { CefGlueNextAvaloniaRuntime.Shutdown(); }
```

---

## 3. 快速开始:5 分钟跑起 Demo

> 假设你已经按 [`ENVIRONMENT.md`](ENVIRONMENT.md) 装好 .NET 10 SDK、pnpm 11、Node.js 20+,并把 CEF 原生运行时放进 `runtime/cef/win-x64/`。

### 3.1 一次性环境初始化

```powershell
# 仓库根
cd F:\Code\tauri.net

# 仅首次需要:下载并校验 CEF 原生运行时
./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64
# 其他 RID:win-arm64 / linux-x64 / linux-arm64 / osx-x64 / osx-arm64

# 还原 + 构建 .NET 解决方案
dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln --no-restore
```

### 3.2 跑 Demo

Demo 位于 `examples/demo/`,它通过 `ProjectReference` 直接链向 `src/`,所以永远跟踪最新源码。

```powershell
# 一键跑起来(Demo.Desktop 内部会调度 pnpm dev)
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj
```

启动后:

1. `CefGlueNextAvaloniaRuntime.RunSubProcess` 先拦截 CEF 子进程调用,避免重复启动 host。
2. `SingleInstanceGuard.Acquire` 把第二次启动的参数转发给主进程并退出,实现单实例。
3. `TaruiHost.CreateApplicationBuilder` 组合 shell + 18 个插件(见 `Demo.Desktop/Program.cs`)。
4. Avalonia 主窗口 1280×820 加载 `examples/demo/web/dist/index.html`,React UI 显示窗口状态、Store、FS、Event、原生侧边栏面板。

---

## 4. 模板脚手架:tarui init

模板包 `Tarui.Templates` 提供了 `tarui-app` 模板。CLI(`src/tarui-cli`,`tarui` 命令)把脚手架/构建/打包整合成与 `tauri-cli` 同构的体验。

```powershell
# 1. 安装 CLI(发布后)
dotnet tool install -g Tarui.Cli

# 2. 新建应用
tarui init my-app
cd my-app

# 3. 开发模式:同时跑 pnpm dev 与 dotnet run
tarui dev

# 4. 生产构建:pnpm build → dotnet publish → zip/msix
tarui build
```

CLI 是 **纯编排器**(零第三方依赖,只读 `tarui.app.json`、spawn 子进程、传递环境变量、校验产物),所以开发者机器和 CI 表现一致。

### 4.1 `tarui.app.json` 构建清单

Demo 自带一份可参考的清单(`examples/demo/tarui.app.json`):

```json
{
  "$schema": "https://tarui.dev/schemas/app.v1.json",
  "product": { "name": "demo", "version": "0.1.0", "identifier": "dev.demo" },
  "build": {
    "frontend": "web",
    "beforeDevCommand": "pnpm dev",
    "devUrl": "http://localhost:5173",
    "beforeBuildCommand": "pnpm build",
    "frontendDist": "web/dist",
    "desktopProject": "Demo.Desktop/Demo.Desktop.csproj"
  },
  "bundle": { "targets": ["zip"] },
  "app": { "capabilities": ["main", "editor"] }
}
```

字段语义:

| 字段 | 作用 |
| --- | --- |
| `product.name` / `product.identifier` | 应用显示名与反写域名标识 |
| `build.frontend` | 前端目录(相对应用根) |
| `build.beforeDevCommand` / `build.devUrl` | dev 模式前置命令、Vite dev server URL(`Tarui:Web:Mode` 自动推断为 `http`) |
| `build.beforeBuildCommand` / `build.frontendDist` | 生产构建前置命令、产物目录(Scheme 模式走 `tarui://localhost`) |
| `build.desktopProject` | 桌面宿主项目相对路径 |
| `bundle.targets` | 安装器目标:`zip`(必选)、`msix`(Windows 商店风格) |
| `app.capabilities` | 应用加载的 capability 文件名(不含 `.json`) |

完整 schema 见 [`schemas/tarui-app.schema.json`](../schemas/tarui-app.schema.json)。

---

## 5. 运行时配置:`appsettings.json`

模板生成的 `appsettings.json` 是宿主运行时配置,优先级:**默认值 < `Tarui:Window:*` 配置 < `builder.Window` 代码配置**。

```json
{
  "Tarui": {
    "Application": { "DeepLinkSchemes": ["tarui"] },
    "Window": { "Title": "Tarui Demo", "Width": 1280, "Height": 820 },
    "Web": {
      "Policy": { "NavExternal": "https:*" }
    }
  },
  "Logging": { "LogLevel": { "Default": "Information" } }
}
```

常用键(`Tarui:Window:*`):

| 键 | 作用 | 默认 |
| --- | --- | --- |
| `Title` | 主窗口标题 | `tarui.net` |
| `Url` | 主窗口 URL | 空 → 推断自前端 |
| `Width` / `Height` | 尺寸 | `1280` / `820` |
| `MinWidth` / `MinHeight` | 最小尺寸 | `900` / `600` |
| `X` / `Y` / `Center` | 启动位置 / 居中 | 居中 |
| `Resizable` / `Decorations` / `AlwaysOnTop` / `Visible` | 窗口外观 | `true` / `true` / `false` / `true` |

WebView 资源模式(`Tarui:Web:*`,环境变量 `TARUI_WEB_*` 兜底):

| 键 | 取值 | 说明 |
| --- | --- | --- |
| `Mode` | `http` / `scheme` | 自动推断。`http` 适合 Vite dev 或本地服务器;`scheme` 把 `tarui://localhost` 直接映射到 `frontendDist`,无需 HTTP listener |
| `Url` | `http(s)://...` | 仅 HTTP 模式 |
| `Root` / `Scheme` / `Host` | 路径与 scheme 名 | 仅 Scheme 模式 |
| `SpaFallback` | `true`/`false` | 仅当扩展名缺失的主帧导航 404 时回退到 `index.html` |
| `Csp` | CSP 字符串 | Scheme 模式强制应用 |
| `MaxAssetBytes` | 整数 | 静态资源大小上限 |

数值/布尔用 `InvariantCulture` 解析,非法值直接抛错(fail fast)。

---

## 6. 能力清单:`capabilities/*.json`

能力清单是 **IPC 权限闸门**。每个窗口/WebView 持有标识符(如 `main`),只允许执行其 capability 中显式列入的命令与事件。缺省即拒绝。

`capabilities/main.json` 结构(节选自 demo):

```json
{
  "identifier": "main",
  "windows": ["main"],
  "platforms": ["windows", "macos", "linux"],
  "events": [
    "window://moved", "window://resized", "window://focus-changed",
    "window://close-requested", "shell://theme-changed",
    "tray://clicked", "notification://activated",
    "deeplink://tarui", "updater://status", "demo://echo"
  ],
  "permissions": [
    "core:app|get-info",
    "core:window|set-title",
    "core:window|set-size",
    {
      "identifier": "plugin:store|set",
      "allow": [
        { "base": "appData", "path": "settings.json" },
        { "base": "appConfig", "path": "**/*.json" }
      ]
    }
  ]
}
```

关键规则:

- **`permissions`** 字段支持两种形式:字符串(允许完整权限)或对象(细化 scope,例如文件/快捷键/store 路径)。
- **`events`** 控制窗口订阅哪些事件;`window://*` 与 `shell://theme-changed` 等是 shell 主动发出的;`user://` 不携带原生数据。
- **Scope deny 优先**:FileSystem、GlobalShortcut、Store 等插件支持 `allow` 与 `deny` 同存,deny 命中即拒绝。
- **多窗口隔离**:`create-window` 时只在两者 ID 与 events/permissions 完全匹配时才允许。
- **协作式关闭**:title-bar 关闭请求先发出 `window://close-requested` 事件,前端必须显式调用 `core:window|close` 才真正退出。

完整 schema 见 [`schemas/tarui-desktop-capability.schema.json`](../schemas/tarui-desktop-capability.schema.json)。

---

## 7. 前端桥接:`@lytree/api`

> 前端包 `@lytree/api`(位于 `web/packages/api/`)提供与后端命令一一对应的 TypeScript 模块,主入口和子路径导出均支持:


```ts
import { invoke } from "@lytree/api/ipc"
import { getAppInfo } from "@lytree/api/app"
import { getCurrentWindow } from "@lytree/api/window"
import { emit, listen } from "@lytree/api/event"
import { openDialog } from "@lytree/api/dialog"        // 注:openExternal 也在 dialog barrel
import { openExternal } from "@lytree/api/shell"      // OS 默认处理器
import { fs, store, log } from "@lytree/api/fs"       // 命名空间风格
```

`Window.getCurrent()` 无 label 时指向当前 Webview 所在窗口,`getByLabel('editor')` 寻址其它窗口。生命周期订阅(`onMoved`、`onResized`、`onFocusChanged`、`onCloseRequested`、`onDestroyed`)统一包装 `listen`。失败以 `IpcCommandError` 抛出,携带路由器错误码。

Demo 前端(`examples/demo/web/src/App.tsx`)演示了:

- `getAppInfo()` 拿壳握手元数据。
- `getCurrentWindow().getState()` 轮询窗口状态;`onMoved`/`onResized`/`onFocusChanged` 订阅变化。
- `store.set / get / keys` 操作 `appData/settings.json`。
- `fs.readDir({ base: 'appData' })` 列出隔离目录。
- `emit('demo://echo', payload)` 触发路由事件。

### 7.1 错误码语义

| 错误码 | 含义 |
| --- | --- |
| `not_authorized` | 窗口 capability 未授予该命令 |
| `not_found` | 命令未注册或窗口不存在 |
| `invalid_payload` | DTO 校验失败 |
| `scope_denied` | scope allow/deny 命中拒绝 |

---

## 8. CLI 命令面(tarui)

`Tarui.Cli`(已发布为 `tarui` 命令,项目入口 `src/tarui-cli/`)的命令形态对齐 `tauri-cli`:

| 命令 | 作用 |
| --- | --- |
| `tarui init <name> [--local <path>]` | 脚手架新应用;`--local` 指向 tarui 源码,启用 `ProjectReference` 开发模式 |
| `tarui dev` | 编排 Vite dev server + `dotnet run`,自动注入 `TARUI_WEB_URL` |
| `tarui build [--bundle zip,msix]` | `pnpm build` + `dotnet publish` + 安装器打包 |
| `tarui info` | 打印 SDK 版本、目标 RID、清单解析结果 |
| `tarui plugin init <name>` / `tarui plugin pack` | 插件脚手架与双包(nupkg + tgz)校验 |

CLI 是 **零第三方依赖** 的纯编排器,所有编译/打包仍交给原生工具链(pnpm、dotnet、signtool)。

---

## 9. 桌面发布:MSIX 与 zip

`tarui build --bundle zip,msix` 会:

1. 调 `dotnet pack` 把 21 个生产项目打成 `artifacts/nuget/*.nupkg`。
2. `dotnet publish` 出 `Demo.Desktop.dll` + Avalonia 依赖。
3. 把 `frontendDist` 复制到发布目录,作为 Scheme 模式根。
4. 用 `MsixPacker`(纯托管实现,**不依赖 `makeappx.exe`)** 生成 `AppxManifest.xml` + `AppxBlockMap.xml`(SHA-256 分块哈希)+ 完整载荷。
5. 若配置了 `WINDOWS_CERT_*` 密钥,调用 `signtool.exe` 做 `/fd SHA256` + 可选时间戳;否则产未签名 MSIX。
6. 同时产出 `dist/<app>-<rid>-<version>.zip` 通用安装器。

CI 配置详见 `.github/workflows/release.yml`:tag `tarui-v<version>` 触发 → 完整打包门禁 → OIDC trusted publishing 推 nuget 与 npm → 创建 GitHub Release。

---

## 10. 故障排查速查

| 现象 | 原因 / 处置 |
| --- | --- |
| 启动后白屏 | `frontendDist` 路径错误,或 Scheme 模式下忘记构建前端;检查 `appsettings.json` 的 `Tarui:Web:Root` 与 `runtime/cef/<rid>/` 是否存在 |
| `NotAuthorized` 抛出 | 调用方窗口 capability 未授权;对照 `capabilities/<window>.json` 的 `permissions` 数组补全 |
| IPC 无响应 | 命令名拼写不一致、TaruiJsonContext 未注册该 DTO、Capability `permissions` 缺对应 ID |
| CEF 子进程无限递归 | 检查 `RunSubProcess` 是否在 host builder 之前调用 |
| 主进程退出时崩溃 | Avalonia 关闭时还有 WebView 未 `CloseAsync`,违反生命周期顺序;参见 [`architecture.md` §生命周期顺序](architecture.md) |
| MSIX 安装失败 | 证书未签名或 publisher 不匹配;先部署未签名版本调试 |
| `dotnet build` 报 0 warnings | `TreatWarningsAsErrors=true` 触发;补缺失注释或 `#pragma warning disable` 仅在已记录情况下使用 |

---

## 11. 进阶阅读

- [`architecture.md`](architecture.md) — 完整所有权边界、IPC 模型、生命周期顺序。
- [`hosting.md`](hosting.md) — `TaruiHost` 设计、配置键全表、Hosting/Shell 分层。
- [`dev-workflow-design.md`](dev-workflow-design.md) — CLI/SDK/插件双包分发的设计稿。
- [`tauri-desktop-alignment-plan.md`](tauri-desktop-alignment-plan.md) — 与 Tauri v2 桌面能力对齐的进度表。
- [`project-optimization-audit-2026-08-24.md`](project-optimization-audit-2026-08-24.md) — 当前基线审计与 P0/P1 优化项。
