# 5 分钟快速上手

> 假设你已经按 [`../dev/environment.md`](../dev/environment.md) 装好 .NET 10 SDK、pnpm 11、Node.js 20+,并把 CEF 原生运行时放进 `runtime/cef/win-x64/`。

## 1. 跑仓库内 Demo

`examples/demo/` 是仓库内的组合根,它通过 `ProjectReference` 直接链向 `src/`,所以永远跟踪最新源码。

```powershell
# 一键跑起来(Demo.Desktop 内部会调度 pnpm dev)
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj
```

启动后:

1. `CefGlueNextAvaloniaRuntime.RunSubProcess` 先拦截 CEF 子进程调用,避免重复启动 host。
2. `SingleInstanceGuard.Acquire` 把第二次启动的参数转发给主进程并退出,实现单实例。
3. `TaruiHost.CreateApplicationBuilder` 组合 shell + 23 个插件(见 `Demo.Desktop/Program.cs`)。
4. Avalonia 主窗口 1280×820 加载 `examples/demo/web/dist/index.html`,React UI 显示窗口状态、Store、FS、Event、原生侧边栏面板、Toast、Channel 等 30 个前端模块。

## 2. 用模板新建应用

```powershell
# 1. 安装 CLI(发布后)
dotnet tool install -g Tarui.Cli

# 2. 新建应用
tarui init my-app
cd my-app

# 3. 开发模式:同时跑 pnpm dev 与 dotnet run
tarui dev

# 4. 生产构建:pnpm build → dotnet publish → zip/msix/.app
tarui build
```

CLI 是 **纯编排器**(零第三方依赖,只读 `tarui.app.json`、spawn 子进程、传递环境变量、校验产物),所以开发者机器和 CI 表现一致。

## 3. 在既有 .NET 项目中嵌入 Tarui

```powershell
# 安装包
dotnet add package Tarui.Hosting
dotnet add package Tarui.Shell
dotnet add package Tarui.WebView.CefGlueNext
dotnet add package Tarui.Plugins.Core
dotnet add package Tarui.Plugins.Window
dotnet add package Tarui.Plugins.Dialog
dotnet add package Tarui.Plugins.System
```

`Program.cs`:

```csharp
using Tarui.Hosting;
using Tarui.Plugins.Core;
using Tarui.Plugins.Window;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.System;
using Tarui.Shell;
using Tarui.WebView.CefGlueNext;

if (CefGlueNextAvaloniaRuntime.RunSubProcess(args)) return;

var builder = TaruiHost.CreateApplicationBuilder(args);
builder.Services
    .AddTaruiShell()
    .AddCefGlueWebView()
    .AddCorePlugin()
    .AddWindowPlugin()
    .AddDialogPlugin()
    .AddSystemPlugin();

builder.Window.Configure(w =>
{
    w.Title = "My App";
    w.Width = 1280;
    w.Height = 820;
});

try { builder.Build().Run(); }
finally { CefGlueNextAvaloniaRuntime.Shutdown(); }
```

## 4. 下一步

- [`scaffolding.md`](scaffolding.md) —— 详细了解 `tarui init` 模板与项目结构
- [`configuration.md`](configuration.md) —— `appsettings.json` 配置键全表
- [`capabilities.md`](capabilities.md) —— IPC 权限清单(必须授权才能调用命令)
- [`plugins.md`](plugins.md) —— 启用插件与 scope 配置
- [`frontend-bridge.md`](frontend-bridge.md) —— `@lytree/api` 前端桥接
- [`cli.md`](cli.md) —— `tarui dev` / `tarui build` 完整命令面
- [`build-and-publish.md`](build-and-publish.md) —— 安装包产物与签名
