# 调试与诊断技巧

> 常见问题排查、调试工具、问题定位路径。

## 1. 调试工具与路径

| 场景 | 工具 / 路径 |
| --- | --- |
| IPC 命令路由追踪 | `Tarui.Plugins.Tests` / `tests/Tarui.Ipc.Tests` 中对照 `CommandRouter.RegisteredPermissions` |
| Capability 拒绝原因 | 在 `CommandRouterComposer` 临时输出 `permission / window capability / scope match` 三元组 |
| CEF 子进程行为 | `--type=` 参数;`CefGlueNextAvaloniaRuntime.RunSubProcess` 必须先于 builder |
| Avalonia 视觉树 | 在 `TaruiAvaloniaApp.OnFrameworkInitializationCompleted` 之前 attach DevTools(Avalonia 12 已 GA DevTools) |
| `dotnet build` 警告爆炸 | 检查 `Directory.Build.props` 的 `NoWarn` 与新增项目;任何 `#pragma warning disable` 必须有 PR 描述说明 |
| 测试基线不达标 | `eng/test-all.ps1 -BaselineCount 22` 临时提高基线找缺漏测试;最终值在 PR 中评审后调整 |
| `MsixPacker` 异常 | 检查 `examples/demo/tarui.app.json` 的 `bundle.msix.publisher` 与 `PublisherDisplayName` 是否匹配 |
| pnpm 锁定漂移 | `pnpm install --frozen-lockfile` 失败时先 `pnpm install` 再 `pnpm test`,确认 CI 与本地锁文件一致 |
| WebView 调试 | `plugin:webview|devtools` 打开 CEF DevTools(走 webview 权限门控) |
| `webview://render-process-gone` 调查 | capability events 需含 `webview://render-process-gone`;`status` 映射 `crashed/killed/other` |
| macOS AppleEvent | `Console.app` + `log stream --predicate 'subsystem CONTAINS "AppleEvents"'` |

## 2. 进程模型与日志

```powershell
# 主进程 stdout/stderr
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj --verbosity normal

# CEF 子进程(CEF 自己 fork,日志走 CefSettings.LogFile)
$env:TARUI_WEB_LOG_FILE = "C:\temp\cef.log"
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj

# 远程日志(由 plugin:log 转发到 MEL)
# LogLevel: Information / Debug
$env:TARUI_LOG_LEVEL__DEFAULT = "Debug"
```

## 3. 常见问题速查

| 现象 | 原因 / 处置 |
| --- | --- |
| 启动后白屏 | `frontendDist` 路径错误,或 Scheme 模式下忘记构建前端;检查 `appsettings.json` 的 `Tarui:Web:Root` 与 `runtime/cef/<rid>/` 是否存在 |
| `NotAuthorized` 抛出 | 调用方窗口 capability 未授权;对照 `capabilities/<window>.json` 的 `permissions` 数组补全 |
| IPC 无响应 | 命令名拼写不一致、TaruiJsonContext 未注册该 DTO、Capability `permissions` 缺对应 ID |
| CEF 子进程无限递归 | 检查 `RunSubProcess` 是否在 host builder 之前调用 |
| 主进程退出时崩溃 | Avalonia 关闭时还有 WebView 未 `CloseAsync`,违反生命周期顺序;参见 [`../design/architecture.md`](../design/architecture.md) |
| MSIX 安装失败 | 证书未签名或 publisher 不匹配;先部署未签名版本调试 |
| `dotnet build` 报 0 warnings | `TreatWarningsAsErrors=true` 触发;补缺失注释或 `#pragma warning disable` 仅在已记录情况下使用 |
| `dotnet run --project Demo.Desktop` 黑屏 | CEF runtime 未装或版本不匹配;`runtime/cef/<rid>/Release/cef.dll` 必须存在 |
| `tarui build --bundle msix` 报 `signtool.exe not found` | 配置 `WINDOWS_CERT_*` 或忽略(未签名 MSIX 仍可生成) |
| `tarui build --bundle app-bundle` 失败 | 检查 `bundle.macOS.{bundleId, schemes}` 是否合法;`bundleId` 必须 reverse-DNS |
| macOS 真机 `plutil -lint` 失败 | `InfoPlistBuilder` 输出缺失必备键;对照 `Tarui.Cli.Tests` 覆盖 |
| `core:platform|capabilities` 报 `notification: false` | 当前平台不支持通知;前端应禁用 UI;Windows 已实现,macOS/Linux 诚实降级 |
| Channel 流式背压 | 已有 await `WebviewSession.SendAsync` 自动背压;无需额外机制 |
| File Watch 不触发 | 检查 capability `events` 含 `fs://watch-change`;`plugin:fs|watch` scope 命中 |
| `core:window|deny-close` 不生效 | 必须收到 `window://close-requested` 事件之后才回执;同时未触发超时强制关闭 |

## 4. 性能与诊断

- **CEF 子进程调试**:`plog` / CEF `chrome://inspect` (Windows 需要 `signtool.exe`)。
- **Avalonia DevTools**:Avalonia 12 集成;按 F12 或 `plugin:webview|devtools` 等价。
- **dotnet-trace**:`dotnet trace collect --process-id <pid>` 收集运行时跟踪。
- **dotnet-counters**:`dotnet counters monitor --process-id <pid> --counters Tarui.*`。
- **JSON 元数据诊断**:`dotnet build /p:EmitCompilerGeneratedFiles=true`,在 `obj/Debug/net10.0/generated/Tarui.Ipc.Generators/` 查看生成的 invoker。
