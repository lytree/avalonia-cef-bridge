# 故障排查速查

> 常见问题速查。更多诊断工具见 [`../dev/debugging.md`](../dev/debugging.md)。

## 1. 启动与运行

| 现象 | 原因 / 处置 |
| --- | --- |
| 启动后白屏 | `frontendDist` 路径错误,或 Scheme 模式下忘记构建前端;检查 `appsettings.json` 的 `Tarui:Web:Root` 与 `runtime/cef/<rid>/` 是否存在 |
| `dotnet run --project Demo.Desktop` 黑屏 | CEF runtime 未装或版本不匹配;`runtime/cef/<rid>/Release/cef.dll` 必须存在;`./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64` |
| Avalonia 启动黑屏 | 确认 `runtime/cef/<rid>/` 已安装,Scheme 模式下确认 `frontendDist` 已构建 |
| CEF 子进程无限递归 | 检查 `RunSubProcess` 是否在 host builder 之前调用 |
| 主进程退出时崩溃 | Avalonia 关闭时还有 WebView 未 `CloseAsync`,违反生命周期顺序;参见 [`../design/architecture.md`](../design/architecture.md) |

## 2. IPC 与 Capability

| 现象 | 原因 / 处置 |
| --- | --- |
| `not_authorized` 抛出 | 调用方窗口 capability 未授权;对照 `capabilities/<window>.json` 的 `permissions` 数组补全 |
| IPC 无响应 | 命令名拼写不一致、TaruiJsonContext 未注册该 DTO、Capability `permissions` 缺对应 ID |
| `scope_denied` | scope allow/deny 命中拒绝;检查 `base` / `path` glob 是否被 `deny` 命中 |
| `core:window|deny-close` 不生效 | 必须收到 `window://close-requested` 事件之后才回执;同时未触发超时强制关闭 |
| `create-window` 失败 | 目标 label 的 capability 文件不存在;shell 不回退 main,要求显式声明 |

## 3. Channel 与流式

| 现象 | 原因 / 处置 |
| --- | --- |
| Channel 流式无响应 | 检查 `Channel.onmessage` 是否赋值;`onClose` 是否在 done 时触发;`invoke` 是否传 channel 字段 |
| 流式背压丢帧 | 已有 await `WebviewSession.SendAsync` 自动背压;无需额外机制 |
| HTTP 流式响应乱序 | 服务端实际返回乱序;客户端按 frame 顺序处理 |

## 4. CLI 与构建

| 现象 | 原因 / 处置 |
| --- | --- |
| `tarui init` 失败 | 检查 `tarui.app.json` schema 是否合法;`product.identifier` 必须 reverse-DNS |
| `tarui dev` 卡在 devUrl 探测 | `devUrl` 在 60s 内不可达;检查 Vite 是否启动失败(日志尾部会显示) |
| `tarui build --bundle msix` 报 `signtool.exe not found` | 配置 `WINDOWS_CERT_*` 或忽略(未签名 MSIX 仍可生成) |
| `tarui build --bundle app-bundle` 失败 | 检查 `bundle.macOS.{bundleId, schemes}` 是否合法;`bundleId` 必须 reverse-DNS |
| macOS 真机 `plutil -lint` 失败 | `InfoPlistBuilder` 输出缺失必备键;对照 `Tarui.Cli.Tests` 覆盖 |
| `dotnet pack` 警告 NU5128(缺 README) | 不再警告:`Directory.Build.props` 已强制把仓库根 `README.md` 放进所有可打包包 |
| `dotnet build` 报 0 warnings | `TreatWarningsAsErrors=true` 触发;补缺失注释或 `#pragma warning disable` 仅在已记录情况下使用 |

## 5. 环境与依赖

| 现象 | 处置 |
| --- | --- |
| `dotnet --version` 输出 8.x / 9.x | `global.json` 锁版本,PATH 上有更旧 SDK 时设 `DOTNET_ROOT` 或调整 PATH |
| `dotnet build` 大量 CS1591 警告 | 仓库已全局抑制;若复现说明有项目覆写了 `NoWarn`,检查 `.csproj` |
| `pnpm install` 报 `ERR_PNPM_BAD_PM_VERSION` | `corepack prepare pnpm@11.15.1 --activate`,确保 `pnpm --version` 等于 11.15.1 |
| CEF 安装脚本报 `system tar with bzip2 support is required` | Windows 10 1809+ 自带 `bsdtar`;若使用别名 `tar.exe`(如 Git for Windows),把它从 PATH 移除或调用绝对路径 |
| CEF SHA-1 校验失败 | 检查网络代理;或手动从 https://cef-builds.spotifycdn.com/ 下载并替换 |
| `dotnet run --project Demo.Desktop` 报 `Microsoft.WindowsDesktop.App` 缺失 | SDK 装的是 runtime 而非 SDK,或 Windows 版本 < 10.0.17763 |

## 6. 平台能力

| 现象 | 处置 |
| --- | --- |
| `core:platform\|capabilities` 报 `notification: false` | 当前平台不支持通知;前端应禁用 UI;Windows 已实现,macOS/Linux 诚实降级 |
| macOS / Linux DeepLink 不工作 | 当前仅 Windows / Linux 已真机验证;macOS `NSAppleEventManager` 桥已落地但真机验收待执行(见 [`../adr/0001-macos-deeplink-appleevent-bridge.md`](../adr/0001-macos-deeplink-appleevent-bridge.md)) |
| Autostart 注册失败 | Linux 需 `~/.config/autostart/` 可写;macOS LaunchAgents 需 `~/Library/LaunchAgents/` 可写 |
| Global Shortcut 冲突 | `plugin:global-shortcut\|register` 已被其他应用占用;前端用 `is-registered` 探测 |

## 7. 测试与门禁

| 现象 | 处置 |
| --- | --- |
| 测试被标 `[skip]` | 项目根 `.requires-env.txt` 列出的环境变量未设置;补齐后重跑 |
| `eng/test-all.ps1` 报 `Passed count below baseline` | 检查是否有项目被无意中删除;新加测试需要同步调整 Baseline |
| `tests/Tarui.Architecture.Tests` 失败提示"反射相关 API" | 检查是否新增了 `ActivatorUtilities.CreateInstance`、`Assembly.Load*`、`MethodInfo.Invoke`;新增的 DI 改用 `AddSingleton<T>` |

## 8. 进一步定位

- 启用 Debug 日志:`$env:TARUI_LOG_LEVEL__DEFAULT = "Debug"`
- CEF 自身日志:`$env:TARUI_WEB_LOG_FILE = "C:\temp\cef.log"`
- WebView DevTools:`plugin:webview|devtools` 打开 CEF DevTools(走 webview 权限门控)
- 详细调试工具与路径见 [`../dev/debugging.md`](../dev/debugging.md)
