# Tarui Hosting — ASP.NET Core 风格开发体验

Tarui.Hosting 把 .NET 生态最成熟的托管模式（`HostApplicationBuilder`、DI、Configuration、Logging、`IHostedService` 生命周期）直接引入桌面壳层。Avalonia 定位为原生组件库 + 窗口外壳，`CefGlue.Next.Avalonia` 承载浏览器页面；Tarui WebView 适配层负责 IPC、策略和静态资源加载，不把 CEF 实现类型泄漏到 Shell。

## 最终开发体验（Tarui.App/Program.cs）

```csharp
using Tarui.Hosting;
using Tarui.Plugins.Core;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.Events;
using Tarui.Plugins.System;
using Tarui.Plugins.Window;
using Tarui.Shell;
using Tarui.WebView.CefGlueNext;

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

builder.Build().Run();
```

`builder.Configuration` / `builder.Logging` / `builder.Services` 与 ASP.NET Core 的 `WebApplicationBuilder` 同构：默认加载 `appsettings.json`、环境变量、命令行；`Run()` 阻塞于 Avalonia 桌面生命周期，退出时按 Host 语义停止所有 `IHostedService`。

## 分层与职责

| 项目 | 新增职责 | 新增包依赖 |
| --- | --- | --- |
| `Tarui.Ipc` | `ITaruiPlugin`、`AddPlugin<T>()`、`CommandRouter.RegisteredPermissions` | `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.0 |
| `Tarui.WebView.Abstractions` | UI 无关的导航、脚本、下载、拖放和拖拽区域契约 | 无 Avalonia / 无 CefGlue |
| `Tarui.WebView.Avalonia` | `Control` 承载契约 | Avalonia 12.1.1 + WebView.Abstractions |
| `Tarui.Shell` | `AddTaruiShell()` 组合注册、`CommandRouterComposer`、`ShellWindowFactory`、`MainWindowLauncher`、`ICapabilityProvider`；删除 `ShellBootstrap` | `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.0 |
| `Tarui.Plugins.*`（×5） | 实例化插件类实现 `ITaruiPlugin` + 各自 `Add*Plugin()` 扩展；删除静态 `Register` | `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.0 |
| `CefGlue.Next.Avalonia` | Avalonia 浏览器控件、CEF handler、runtime/subprocess 和关闭完成信号 | Avalonia 12.1.1；包内嵌托管 CefGlue DLL |
| `Tarui.WebView.CefGlueNext` | `AddCefGlueWebView()`、`CefGlueNextWebAppOptions.FromConfiguration(IConfiguration)`、Tarui 事件/策略适配 | DI.Abstractions + Configuration.Abstractions 10.0.0 + CefGlue.Next.Avalonia |
| `Tarui.Hosting`（新建） | `TaruiHost` / `TaruiApplicationBuilder` / `TaruiApplication` / `TaruiAvaloniaApp` / 生命周期桥 | `Microsoft.Extensions.Hosting` 10.0.0 + Avalonia 桌面包 |
| `Tarui.App` | 组合根：builder UX + `appsettings.json` | 经由 Tarui.Hosting |

依赖方向为：`Hosting → Shell → (Ipc, Contracts, WebView.Abstractions, WebView.Avalonia, 插件接口)`；`Tarui.WebView.CefGlueNext → (WebView.Abstractions, WebView.Avalonia, CefGlue.Next.Avalonia)`；插件仍只依赖 `Ipc + Contracts`。架构门禁（禁反射/扫描/动态加载 + ProjectReference 边界 + 组件包内容）继续成立：插件经 `AddPlugin<T>()` 编译期显式注册，与 ASP.NET Core `AddSingleton<TService,TImpl>` 同类，不做任何程序集扫描。

## 类型设计

### Tarui.Ipc

```csharp
public interface ITaruiPlugin
{
    void ConfigureCommands(CommandRouterBuilder commands);
}

public static class TaruiServiceCollectionExtensions
{
    public static IServiceCollection AddPlugin<TPlugin>(this IServiceCollection services)
        where TPlugin : class, ITaruiPlugin
        => services.AddSingleton<ITaruiPlugin, TPlugin>();
}
```

`CommandRouterBuilder` 增加只读属性 `RegisteredPermissions`（已注册命令权限的去重集合；每个插件 `commands.Add(...)` 时自动登记，等价于旧 `registerPermission` 集合）。`CommandRouter` 同步暴露该集合；能力校验（`CommandRouterComposer`）读取的是 builder 上的集合。

### Tarui.Shell — AddTaruiShell()

```csharp
public static IServiceCollection AddTaruiShell(this IServiceCollection services) => services
    .AddSingleton<WindowRegistry>()
    .AddSingleton<IWindowSinkRegistry>(sp => sp.GetRequiredService<WindowRegistry>())
    .AddSingleton<EventHub>()
    .AddSingleton<EventRouter>()
    .AddSingleton<ICapabilityProvider, CapabilitySetProvider>()   // 缓存 CapabilityLoader.Load(BaseDirectory/capabilities)
    .AddSingleton(sp => CommandRouterComposer.Compose(sp))        // 具体类型 CommandRouter 单例
    .AddSingleton<IpcDispatcher>()
    .AddSingleton<IEventSender>(sp => new RoutedEventSender(sp.GetRequiredService<EventRouter>()))
    .AddSingleton<IDialogService, AvaloniaDialogService>()
    .AddSingleton<IClipboardService, AvaloniaClipboardService>()
    .AddSingleton<ShellWindowFactory>()
    .AddSingleton<IWindowService>(sp => new AvaloniaWindowService(
        sp.GetRequiredService<WindowRegistry>(),
        sp.GetRequiredService<ShellWindowFactory>().CreateEntry))
    .AddSingleton<IMainWindowLauncher, MainWindowLauncher>();
```

- `CommandRouterComposer.Compose(IServiceProvider)`：遍历 `GetServices<ITaruiPlugin>()` → `ConfigureCommands(builder)` → 能力校验（capability 文件里出现未注册权限即抛 `InvalidOperationException`，跳过 `"*"`）→ `builder.Build()`。旧 ShellBootstrap 的校验语义原样保留。
- `ShellWindowFactory(IServiceProvider, WindowRegistry, EventRouter, WindowCapabilityResolver, TaruiAppOrigin)`：`CreateEntry(WindowOptions, CommandContext? caller)` 用 `WindowCapabilityResolver` 解析目标 label 的显式 Capability profile（无 profile 抛 `CapabilityNotFoundException`，不再回退 main；带创建者上下文时执行提权防护），随后构建 `CommandContext`、`WebViewHost`、`ShellWindow`、`WireWindowEvents`、`ResolveSource`。`IpcDispatcher` 与 `ITaruiWebViewFactory` 通过 `IServiceProvider` 惰性解析，保持“窗口只会在分发器构建完成后创建”的既有顺序保证。
- `MainWindowLauncher(WindowRegistry, ShellWindowFactory, EventRouter, ICapabilityProvider, WindowOptions)`：校验 `main` 能力存在 → 创建主窗口 → 注册到 registry → 挂接 `ActualThemeVariantChanged` 主题广播。
- 主窗口描述复用契约 `WindowOptions`（Label 固定 `"main"`），由组合根注册为单例。
- Shell 不再注册任何插件命令；System 插件自有服务（`IPathService` 等 ×4）移入 `AddSystemPlugin()`；剪贴板/窗口/对话框/事件发送的 Avalonia 实现仍归 Shell 注册。

### 插件（×5）

```csharp
public sealed class WindowPlugin(IWindowService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new WindowCommands(service);
        commands.Add("core:window|create", TaruiJsonContext.Default.WindowOptions, ..., handlers.CreateAsync, "core:window|create");
        // ... 其余命令与权限与现状一一对应
    }
}

public static class WindowPluginServiceCollectionExtensions
{
    public static IServiceCollection AddWindowPlugin(this IServiceCollection services)
        => services.AddPlugin<WindowPlugin>();
}
```

`CorePlugin`/`EventPlugin`/`DialogPlugin` 同构；`SystemPlugin(IPathService, IOsService, IProcessService, IShellService, IClipboardService)` 的 `AddSystemPlugin()` 额外注册前四个自有服务。所有命令名、权限名、DTO、handler 行为与现状完全一致。

### Tarui.WebView.CefGlueNext

```csharp
public static CefGlueNextWebAppOptions FromConfiguration(IConfiguration configuration);
```

- 配置键：`Tarui:Web:Mode / Url / Root / Scheme / Host / SpaFallback / Csp / MaxAssetBytes`；未设置时回落到 `TARUI_WEB_*` 环境变量键（HostApplicationBuilder 默认已把环境变量并入 Configuration，扁平键同名可见），再回落到现有默认与内容根探测（`FindContentRoot`）。
- 校验/错误消息沿用 `CreateHttp` / `CreateScheme`（两者保留，继续服务测试与显式构造）。
- 多 scheme 支持：`AllowedSchemes` 列出窗口创建与导航可用的全部 scheme。Scheme 模式接受 `自定义 scheme + http + https`（可导航到 dev server）；HTTP 模式默认仅 `http + https`，但配置 `Root`（config/env/显式参数）后同时注册无端口的自定义 scheme（如 `tarui://localhost/`），本地资产与 HTTP 来源并存。`SchemeOrigin` 暴露该无端口来源（无本地资产时为 null），CEF 在 `ContentRoot` 存在时即注册 scheme 处理器，不限于 Scheme 模式。
- `AddCefGlueWebView(this IServiceCollection)`：注册 `CefGlueNextWebAppOptions`（工厂 `FromConfiguration`）+ `ITaruiWebViewFactory` + `TaruiAppOrigin(options.StartUri, options.AllowedSchemes, options.SchemeOrigin)`。工厂保持惰性单例——首次解析发生在 UI 线程创建主窗口时，CEF 初始化时序不变。另有 `AddCefGlueWebView(this IServiceCollection, CefGlueNextWebAppOptions)` 显式重载，供测试与显式构造使用。

### Tarui.Hosting

```csharp
public static class TaruiHost
{
    public static TaruiApplicationBuilder CreateApplicationBuilder(string[]? args = null);
}

public sealed class TaruiApplicationBuilder
{
    public ConfigurationManager Configuration { get; }   // 来自内部 HostApplicationBuilder
    public IServiceCollection Services { get; }
    public ILoggingBuilder Logging { get; }
    public TaruiWindowBuilder Window { get; }
    public TaruiApplication Build();
}

public sealed class TaruiWindowBuilder  // Title/Width/Height/MinWidth/MinHeight/Center/Url + Configure(Action)
public sealed class TaruiApplication     // Services；StartAsync/StopAsync；Run() 阻塞
```

- 内核即 `HostApplicationBuilder`（`HostApplicationBuilderSettings { Args, ContentRootPath = AppContext.BaseDirectory }`）：Configuration/Logging/DI/IHostEnvironment/`IHostApplicationLifetime`/`IHostedService` 全部获得 .NET 默认行为。桌面应用 ContentRoot 显式固定到 `AppContext.BaseDirectory`（`appsettings.json`、能力文件均从该目录解析）。
- `Build()` 物化主窗口 `WindowOptions`（Label `"main"`）注册为单例；合并优先级：**默认值 < `Tarui:Window:*` 配置 < `builder.Window` 代码配置**。数值/布尔用 InvariantCulture 解析，非法值直接抛错（fail fast）。
- `TaruiApplication.Run()`：
  1. `host.StartAsync()` —— 托管服务按序启动；
  2. `AppBuilder.Configure(() => new TaruiAvaloniaApp(host.Services)).UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args)` —— 阻塞消息循环（Avalonia 12.1.1 提供 `Configure(Func<TApp>)` 工厂重载，已采用，无需静态属性回退）；
  3. `finally`：`host.StopAsync()` + `Dispose()`。
- `TaruiAvaloniaApp`（internal）：`FluentTheme`；`OnFrameworkInitializationCompleted` 注册生命周期桥并 `IMainWindowLauncher.LaunchMainWindow()` 设为主窗口。
- Host 生命周期桥：`TaruiLifetimeBridge`（经 `Register(lifetime)` 持有 `IClassicDesktopStyleApplicationLifetime`，`RequestShutdown()` 经 `Dispatcher.UIThread.Post` 下发）+ `HostShutdownWatcher : IHostedService`（`lifetime.ApplicationStopping.Register(bridge.RequestShutdown)`）。于是 `IHostApplicationLifetime.StopApplication()`（含 Ctrl+C 的 ConsoleLifetime 语义）会真实关闭 UI；窗口关闭 → Avalonia 退出 → Host 正常 Stop。`TaruiApplicationBuilder` 默认注册桥与 watcher。

## 配置键一览

| 键 | 作用 | 默认 |
| --- | --- | --- |
| `Tarui:Window:Title` | 主窗口标题 | `tarui.net` |
| `Tarui:Window:Url` | 主窗口相对/同源 URL | 空 → `TaruiAppOrigin.StartUri` |
| `Tarui:Window:Width` / `Height` | 主窗口尺寸 | 1280 / 820 |
| `Tarui:Window:MinWidth` / `MinHeight` | 最小尺寸 | 900 / 600 |
| `Tarui:Window:MaxWidth` / `MaxHeight` | 最大尺寸 | 不限 |
| `Tarui:Window:X` / `Y` | 启动屏幕位置（逻辑像素） | 居中 |
| `Tarui:Window:Center` | 居中启动 | true |
| `Tarui:Window:Resizable` | 允许缩放 | true |
| `Tarui:Window:Decorations` | 系统标题栏/边框 | true |
| `Tarui:Window:AlwaysOnTop` | 置顶 | false |
| `Tarui:Window:Visible` | 启动即显示 | true |
| `Tarui:Web:Mode` | `http` / `scheme` | 自动推断 |
| `Tarui:Web:Url` / `Root` / `Scheme` / `Host` / `SpaFallback` / `Csp` / `MaxAssetBytes` | WebView 资源模式参数 | 对应 `TARUI_WEB_*` 环境变量默认 |
| `Logging:LogLevel:*` | 标准 M.E.Logging | 框架默认 |

## 不变式（实现与评审红线）

1. 线上 IPC 契约（命令名/权限名/DTO/TaruiJsonContext）零变更；前端 `@lytree/api` 不动。
2. 无反射、无程序集扫描、无动态插件加载；`AddPlugin<T>()` 为编译期显式注册。架构门禁（含禁 `Activator`/`MethodInfo`）必须持续通过，因此**不得使用 `ActivatorUtilities`**。
3. `CommandContext` 标签权威、能力校验、协作式关闭等既有架构不变量原样保留。
4. 每阶段结束：`dotnet build tarui.net.sln --no-restore` 零警告（TreatWarningsAsErrors）、相关测试套件全绿，再进入下一阶段。
5. `CefGlueNextAvaloniaRuntime.RunSubProcess(args)` 永远先于 Host 构建；所有 WebView 完成 native close 后，Avalonia loop 退出、Host `StopAsync` 与 `Dispose` 完成，最后由 `Program` 的 `finally` 调用 runtime `Shutdown`。

## CEF/Avalonia 生命周期

```text
RunSubProcess(args)
  -> Host.StartAsync / Avalonia lifetime
  -> CefGlueNextAvaloniaRuntime.Initialize(options)
  -> create Tarui WebViews
  -> close windows and await WebView CloseAsync
  -> Avalonia loop exits
  -> Host StopAsync / Dispose
  -> Program finally: CefGlueNextAvaloniaRuntime.Shutdown()
```

直接使用 Avalonia 的应用可以跳过 Tarui 适配层，直接安装 `CefGlue.Next.Avalonia` 并把 `CefGlueNextAvaloniaWebView` 放入视觉树；Tarui 应用则通过 `Tarui.WebView.CefGlueNext` 将同一组件接入窗口 capability、IPC 和资源策略。

## 阶段划分

1. **P1** `Tarui.Ipc` 插件抽象 + `AddPlugin<T>` + `TaruiAppOrigin`（WebView.Abstractions）+ Ipc.Tests。
2. **P2** Shell/插件/App 组合根 DI 重构：`AddTaruiShell` + 5×`Add*Plugin` + Composer/Factory/Launcher，删除 `ShellBootstrap` 与静态 `Register`，App 过渡为手工 `ServiceCollection` 组合；迁移/新增 Shell.Tests、Plugins.Tests。
3. **P3** `CefGlueNextWebAppOptions.FromConfiguration` + `AddCefGlueWebView` + WebView.Tests。
4. **P4** 新建 `Tarui.Hosting` + `tests/Tarui.Hosting.Tests`（builder/配置合并/生命周期/hosted service 顺序/桥）+ sln 注册。
5. **P5** `Tarui.App` 切换 builder UX + `appsettings.json`；全量构建 + 六套测试全绿。
6. **P6** 文档：README.md、docs/architecture.md、AGENTS.md。
