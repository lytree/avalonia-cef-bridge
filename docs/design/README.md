# 设计文档

> 面向 **架构师 / 评审者 / 高级贡献者**:理解所有权边界、IPC 模型、能力授权、生命周期。
>
> 配套文档:[`../usage/`](../usage/README.md)(应用开发者)、[`../dev/`](../dev/README.md)(贡献者)。

## 目录

| 文档 | 内容 |
| --- | --- |
| [`architecture.md`](architecture.md) | 仓库内所有权边界、分层依赖、模块生命周期 |
| [`hosting.md`](hosting.md) | `Tarui.Hosting` / `TaruiApplicationBuilder` / 配置键全表 / Hosting vs Shell 分层 |
| [`ipc.md`](ipc.md) | Command / Event / Channel 三类 IPC、能力闸门、错误码、DTO 元数据 |
| [`plugin-system.md`](plugin-system.md) | 插件契约、`ITaruiPlugin` / `Add*Plugin()`、权限命名、Scope allow/deny、模板 |
| [`webview.md`](webview.md) | 浏览器栈分层、`Tarui.WebView.CefGlueNext` 适配、CefGlue 内置源码边界 |
| [`web-resource-mode.md`](web-resource-mode.md) | HTTP / Scheme 双模式、`TaruiAppOrigin`、Scheme 处理、CSP / SPA fallback |
| [`alignment-plan.md`](alignment-plan.md) | 与 Tauri v2 桌面能力的逐项对齐进度表(权威状态表) |
| [`gap-analysis.md`](gap-analysis.md) | 与 Tauri v2 / Wails v3 的功能差距分析(P0/P1/P2 缺口) |
| [`cli-workflow.md`](cli-workflow.md) | CLI / SDK / 插件双包分发设计稿(W0-W5 阶段记录) |

## ADR 索引

| ADR | 主题 |
| --- | --- |
| [`../adr/0001-macos-deeplink-appleevent-bridge.md`](../adr/0001-macos-deeplink-appleevent-bridge.md) | macOS DeepLink 通过 `NSAppleEventManager` 桥接 `DeepLinkService` |
| [`../adr/0002-macos-real-build-pipeline.md`](../adr/0002-macos-real-build-pipeline.md) | macOS 真机构建管道(`.app` + `app-bundle` target) |

## 关键设计不变量

任何贡献者改动都必须维持的不变量(由 `Tarui.Architecture.Tests` 静态扫描守护):

1. **无反射**:`Tarui.*` 程序集不得引用 `System.Reflection.Emit`、`Activator`、`MethodInfo` 等;**严禁 `ActivatorUtilities`**(避免隐式反射 DI)。
2. **显式插件注册**:插件只通过 `AddPlugin<T>()` / `Add*Plugin()` 编译期注入;禁止 `AppDomain.GetAssemblies()` 等扫描手段。
3. **零运行时依赖 Xilium**:除 `Tarui.WebView.CefGlueNext` 自身,其他 Tarui 项目不得引用 `Xilium.CefGlue*`。
4. **源生成 JSON**:跨进程 DTO 走 `JsonSerializerContext` 静态元数据,禁止反射回退的 `JsonSerializer.Serialize(obj)` 路径。
5. **能力闸门强制**:每个命令进入路由器都需经 `CommandRouterComposer` 比对 `RegisteredPermissions` ∩ 窗口 capability。
6. **生命周期顺序**:`RunSubProcess` → `Host.StartAsync`/`Avalonia lifetime` → CEF `Initialize` → 创建 WebView → 关闭窗口 → `WebView.CloseAsync` 全部完成 → Avalonia loop 退出 → `Host.StopAsync` + `Dispose` → `finally: CefGlueNextAvaloniaRuntime.Shutdown`。
7. **TreatWarningsAsErrors=true**:缺注释警告 CS1591/CS1572/CS1573/CS1574/CS1711/CS1712/CS1734 在 `Directory.Build.props` 已抑制。
8. **版本单源**:`TaruiVersion` 在 `Directory.Build.props`,所有可打包项目与其一致;CI 校验等于 `@lytree/api` 的 `package.json` 版本。
9. **Lockstep 发布**:`Tarui.*` NuGet 与 `@lytree/api` npm lockstep 推进,变更须同步升级。
10. **零外部发布依赖**:CLI 零第三方依赖,仅 BCL + `System.Text.Json` 源生成。
