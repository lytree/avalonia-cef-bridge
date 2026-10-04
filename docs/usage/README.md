# 使用文档

> 面向 **应用开发者**:用 tarui.net 构建一个跨平台桌面应用应当如何接线、跑起来、打包发布。
>
> 配套文档:[`../design/`](../design/README.md)(架构与评审)、[`../dev/`](../dev/README.md)(框架贡献者)。

## 目录

| 文档 | 内容 |
| --- | --- |
| [`quickstart.md`](quickstart.md) | 5 分钟跑起 Demo / 模板脚手架 |
| [`scaffolding.md`](scaffolding.md) | `tarui init` 与 `dotnet new tarui-app` 模板 |
| [`configuration.md`](configuration.md) | `appsettings.json` 与 `Tarui:Window:*` / `Tarui:Web:*` 配置键 |
| [`capabilities.md`](capabilities.md) | `capabilities/*.json` 权限清单(IPC 闸门) |
| [`plugins.md`](plugins.md) | 插件使用、Scope allow/deny、常见插件速查 |
| [`frontend-bridge.md`](frontend-bridge.md) | `@lytree/api` 前端桥接、错误码、Channel |
| [`webview-resource-mode.md`](webview-resource-mode.md) | HTTP / Scheme 双模式、CSP、SPA fallback |
| [`cli.md`](cli.md) | `tarui` 命令行工具(init / dev / build / plugin) |
| [`build-and-publish.md`](build-and-publish.md) | zip / MSIX / `.app` / `.app.tar.gz` 安装包产物,签名 |
| [`troubleshooting.md`](troubleshooting.md) | 常见问题排查速查 |

## 三种使用姿势

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
