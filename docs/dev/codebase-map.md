# 代码库地图

> 仓库内项目分层、模块职责、依赖方向、关键设计不变量。

## 1. 仓库结构

仓库根由 50+ 个可 restore 的项目组成,组织如下:

```text
src/
  core/
    Tarui.Contracts/        # 零依赖契约层:IPC DTO、错误码、JsonSerializerContext
    Tarui.Ipc/              # 运行时:CommandRouter、ITaruiPlugin、AddPlugin<T> 扩展
  desktop/
    Tarui.Hosting/          # ASP.NET Core 风格主机:TaruiHost/Builder/Lifetime
    Tarui.Shell/            # 声明式壳组合:AddTaruiShell、WindowRegistry、EventRouter
    Tarui.SingleInstance/   # 单实例守卫与 IPC 转发
  generators/
    Tarui.Ipc.Generators/   # Roslyn 源生成器:JSON 元数据、强类型 invoker
  plugins/
    Tarui.Plugins.*/        # 23 个独立插件项目(详见 ./plugin-development.md)
  webview/
    Tarui.WebView.Abstractions/  # UI 中立的导航/脚本/下载/拖放契约
    Tarui.WebView.Avalonia/      # Avalonia Control 承载契约
    Tarui.WebView.CefGlueNext/   # Tarui 适配层 + 浏览器组件(原 CefGlue.Next.Avalonia 已并入;包内嵌 5 个 Xilium DLL)
    cefglue/                     # 内置第三方源码,尽量少改动
  tarui-cli/                # CLI 工具项目(Tarui.Cli → tarui 命令)
  templates/Tarui.Templates/    # dotnet new tarui-app 模板
tests/                      # 控制台式自测试(*.Tests.csproj)
examples/demo/              # 仓库内组合根 Demo
web/                        # 前端 pnpm 工作区
eng/                        # 工程脚本(CEF 安装、test-all 调度)
schemas/                    # tarui.app.json 与 capability 的 JSON schema
docs/                       # 文档(design / dev / usage / adr)
capabilities/               # Demo/编辑器用能力清单
.github/workflows/          # CI / Release 工作流
```

## 2. 模块职责

| 项目 | 关键类型 | 职责 |
| --- | --- | --- |
| `Tarui.Contracts` | DTO record、`TaruiJsonContext`(`JsonSerializerContext`) | 跨进程序列化契约,零运行时依赖 |
| `Tarui.Ipc` | `ITaruiPlugin`、`AddPlugin<T>()`、`CommandRouterBuilder`、`IpcDispatcher` | 插件抽象、命令路由器、权限登记(`RegisteredPermissions`) |
| `Tarui.Shell` | `AddTaruiShell`、`WindowRegistry`、`EventRouter`、`CapabilitySetProvider`、`ShellWindowFactory`、`MainWindowLauncher`、`IpcDispatcher` 接入 | 声明式组合,所有插件均 `ProjectReference` 引入 |
| `Tarui.Hosting` | `TaruiHost.CreateApplicationBuilder`、`TaruiApplicationBuilder`、`TaruiApplication`、`TaruiAvaloniaApp`、`TaruiLifetimeBridge`、`HostShutdownWatcher` | 注入 M.E.Hosting、Avalonia lifecycle 桥接 |
| `Tarui.SingleInstance` | `SingleInstanceGuard`、`SingleInstanceIdentity`、`InstanceRole` | 二次启动参数转发到主进程 |
| `Tarui.WebView.Abstractions` | `IWebViewHost`、`INavigationRequest`、`IDownloadRequest` | UI 中立契约,无 Avalonia/CefGlue |
| `Tarui.WebView.Avalonia` | `TaruiWebView`(Avalonia Control) | Control 承载层 |
| `Tarui.WebView.CefGlueNext` | `AddCefGlueWebView()`、`CefGlueNextWebAppOptions`、`CefGlueNextAvaloniaWebView`、`CefGlueNextAvaloniaRuntime` | Tarui 事件/策略/Capability 适配 + 浏览器组件实现,nupkg 内嵌 Xilium CefGlue DLL(原 `CefGlue.Next.Avalonia` 已并入) |
| `Tarui.Ipc.Generators` | `IIncrementalGenerator` | 源生成 TaruiJsonContext 与强类型 invoker |

依赖方向(强约束,被架构门禁检查):

```text
Hosting  →  Shell  →  (Ipc, Contracts, WebView.Abstractions, WebView.Avalonia, 插件接口)
                          ↑
              CefGlueNext  →  (WebView.Abstractions, WebView.Avalonia)
                          ↑
                  Tarui.WebView.CefGlueNext(包内嵌 5 个 Xilium DLL)
```

`Hosting` 和 `Shell` 都不引用 Xilium CefGlue 程序集;`Tarui.WebView.CefGlueNext` 是唯一接触 CefGlue 实现类型的项目;`webview/cefglue/*` 只能被 `Tarui.WebView.CefGlueNext` 引用。

## 3. 关键设计不变量

任何改动必须维持的不变量(由 `Tarui.Architecture.Tests` 检查):

1. **无反射**:`Tarui.*` 程序集不得引用 `System.Reflection.Emit`、`Activator`、`MethodInfo` 等;**严禁 `ActivatorUtilities`**(避免隐式反射 DI)。
2. **显式插件注册**:插件只通过 `AddPlugin<T>()` / `Add*Plugin()` 编译期注入;禁止 `AppDomain.GetAssemblies()` 等扫描手段。
3. **零运行时依赖 Xilium**:除 `Tarui.WebView.CefGlueNext`(原 `CefGlue.Next.Avalonia` 已并入此包)自身,其他 Tarui 项目不得引用 `Xilium.CefGlue*`。
4. **源生成 JSON**:跨进程 DTO 走 `JsonSerializerContext` 静态元数据,禁止反射回退的 `JsonSerializer.Serialize(obj)` 路径。
5. **能力闸门强制**:每个命令进入路由器都需经 `CommandRouterComposer` 比对 `RegisteredPermissions` ∩ 窗口 capability。
6. **生命周期顺序**:`RunSubProcess` → `Host.StartAsync` / `Avalonia lifetime` → CEF `Initialize` → 创建 WebView → 关闭窗口 → `WebView.CloseAsync` 全部完成 → Avalonia loop 退出 → `Host.StopAsync` + `Dispose` → `finally: CefGlueNextAvaloniaRuntime.Shutdown`。
7. **TreatWarningsAsErrors=true**:缺注释警告 CS1591/CS1572/CS1573/CS1574/CS1711/CS1712/CS1734 在 `Directory.Build.props` 已抑制;新增注释规范后续统一补齐。
8. **版本单源**:`TaruiVersion` 在 `Directory.Build.props`,所有可打包项目与其一致;CI 校验等于 `@lytree/api` 的 `package.json` 版本。
9. **Lockstep 发布**:`Tarui.*` NuGet 与 `@lytree/api` npm lockstep 推进,变更须同步升级。
10. **零外部发布依赖**:CLI 零第三方依赖,仅 BCL + `System.Text.Json` 源生成。

## 4. 修改 Shell 或 Hosting 的步骤

1. **同步分层**:`Shell` 是 `Hosting` 的下层;`Hosting` 不能引用 `Shell`,`Shell` 反向可独立使用(`ShellBootstrap` 已被删除,改走 DI)。
2. **生命周期桥**:任何对 Avalonia `IClassicDesktopStyleApplicationLifetime` 的触碰必须经 `TaruiLifetimeBridge` + `HostShutdownWatcher`,避免破坏 Ctrl+C / 窗口关闭的桥接。
3. **窗口配置合并**:窗口默认值 < `Tarui:Window:*` 配置 < `builder.Window` 代码配置;数值/布尔用 `InvariantCulture` 解析,**非法值必须 fail fast**。
4. **能力校验位置**:新增命令必须经 `CommandRouterBuilder.Add(...)` 注册;`CommandRouter.RegisteredPermissions` 会自动收录该命令的 permission ID;任何手工登记都会被架构门禁视为可疑。
5. **架构门禁**:`Tarui.Architecture.Tests` 用 Roslyn 扫描 `src/` 下 active 文件,任何引入 `ActivatorUtilities`、`Assembly.Load*`、`GetAssemblies`、反射 JSON 路径都会被 CI 拒。
