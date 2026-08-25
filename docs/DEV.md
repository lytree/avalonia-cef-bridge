# tarui.net 开发文档

> 面向 **框架贡献者**:理解代码库分层、扩展插件、修改 Shell/Hosting/Ipc、维护架构门禁的指南。
>
> 配套文档:[`USAGE.md`](USAGE.md)(应用开发者视角)、[`ENVIRONMENT.md`](ENVIRONMENT.md)(环境初始化)、[`architecture.md`](architecture.md)(架构总览)、[`hosting.md`](hosting.md)(托管层详解)、[`dev-workflow-design.md`](dev-workflow-design.md)(CLI/SDK 分发设计稿)、[`tauri-desktop-alignment-plan.md`](tauri-desktop-alignment-plan.md)(能力对齐进度)。

---

## 1. 代码库地图

仓库根 `F:\Code\tauri.net` 由 56 个可 restore 的项目组成,组织如下:

```
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
    Tarui.Plugins.*/        # 18 个独立插件项目(详见 §4)
  webview/
    Tarui.WebView.Abstractions/  # UI 中立的导航/脚本/下载/拖放契约
    Tarui.WebView.Avalonia/      # Avalonia Control 承载契约
    Tarui.WebView.CefGlueNext/   # Tarui 适配层:连接 IPC 与 CefGlue 组件
    CefGlue.Next.Avalonia/       # 浏览器组件实现(组件包,内置 Xilium DLL)
    cefglue/                     # 内置第三方源码,尽量少改动
  tarui-cli/                # CLI 工具项目(Tarui.Cli → tarui 命令)
  templates/Tarui.Templates/    # dotnet new tarui-app 模板
tests/                      # 21 个控制台式自测试(*.Tests.csproj)
examples/demo/              # 仓库内组合根 Demo
web/                        # 前端 pnpm 工作区
eng/                        # 工程脚本(CEF 安装、test-all 调度)
schemas/                    # tarui.app.json 与 capability 的 JSON schema
docs/                       # 设计与审计文档
capabilities/               # Demo/编辑器用能力清单
.github/workflows/          # CI / Release 工作流
```

依赖方向(强约束,被架构门禁检查):

```
Hosting  →  Shell  →  (Ipc, Contracts, WebView.Abstractions, WebView.Avalonia, 插件接口)
                          ↑
              CefGlueNext  →  (WebView.Abstractions, WebView.Avalonia, CefGlue.Next.Avalonia)
                          ↑
                  CefGlue.Next.Avalonia(包内嵌 5 个 Xilium DLL)
```

`Hosting` 和 `Shell` 都不引用 Xilium CefGlue 程序集;`CefGlue.Next.Avalonia` 是唯一接触 CefGlue 实现类型的项目;`webview/cefglue/*` 只能被 `CefGlue.Next.Avalonia` 引用。

---

## 2. 核心分层职责

| 项目 | 关键类型 | 职责 |
| --- | --- | --- |
| `Tarui.Contracts` | DTO record、`TaruiJsonContext`(JsonSerializerContext) | 跨进程序列化契约,零运行时依赖 |
| `Tarui.Ipc` | `ITaruiPlugin`、`AddPlugin<T>()`、`CommandRouterBuilder`、`IpcDispatcher` | 插件抽象、命令路由器、权限登记(`RegisteredPermissions`) |
| `Tarui.Shell` | `AddTaruiShell`、`WindowRegistry`、`EventRouter`、`CapabilitySetProvider`、`ShellWindowFactory`、`MainWindowLauncher`、`IpcDispatcher` 接入 | 声明式组合,所有插件均 `ProjectReference` 引入 |
| `Tarui.Hosting` | `TaruiHost.CreateApplicationBuilder`、`TaruiApplicationBuilder`、`TaruiApplication`、`TaruiAvaloniaApp`、`TaruiLifetimeBridge`、`HostShutdownWatcher` | 注入 M.E.Hosting、Avalonia lifecycle 桥接 |
| `Tarui.SingleInstance` | `SingleInstanceGuard`、`SingleInstanceIdentity`、`InstanceRole` | 二次启动参数转发到主进程 |
| `Tarui.WebView.Abstractions` | `IWebViewHost`、`INavigationRequest`、`IDownloadRequest` | UI 中立契约,无 Avalonia/CefGlue |
| `Tarui.WebView.Avalonia` | `TaruiWebView` (Avalonia Control) | Control 承载层 |
| `Tarui.WebView.CefGlueNext` | `AddCefGlueWebView()`、`CefGlueNextWebAppOptions` | Tarui 事件/策略/Capability 适配 |
| `CefGlue.Next.Avalonia` | `CefGlueNextAvaloniaWebView`、`CefGlueNextAvaloniaRuntime` | 浏览器组件实现,nupkg 内嵌 Xilium CefGlue DLL |
| `Tarui.Ipc.Generators` | `IIncrementalGenerator` | 源生成 TaruiJsonContext 与强类型 invoker |

---

## 3. 关键设计不变量

任何改动必须维持的不变量(由 `Tarui.Architecture.Tests` 检查):

1. **无反射**:`Tarui.*` 程序集不得引用 `System.Reflection.Emit`、`Activator`、`MethodInfo` 等;**严禁 `ActivatorUtilities`**(避免隐式反射 DI)。
2. **显式插件注册**:插件只通过 `AddPlugin<T>()` / `Add*Plugin()` 编译期注入;禁止 `AppDomain.GetAssemblies()` 等扫描手段。
3. **零运行时依赖 Xilium**:除 `CefGlue.Next.Avalonia` 自身及其下游 `Tarui.WebView.CefGlueNext`,其他 Tarui 项目不得引用 `Xilium.CefGlue*`。
4. **源生成 JSON**:跨进程 DTO 走 `JsonSerializerContext` 静态元数据,禁止反射回退的 `JsonSerializer.Serialize(obj)` 路径。
5. **能力闸门强制**:每个命令进入路由器都需经 `CommandRouterComposer` 比对 `RegisteredPermissions` ∩ 窗口 capability。
6. **生命周期顺序**:`RunSubProcess` → `Host.StartAsync`/`Avalonia lifetime` → CEF `Initialize` → 创建 WebView → 关闭窗口 → `WebView.CloseAsync` 全部完成 → Avalonia loop 退出 → `Host.StopAsync` + `Dispose` → `finally: CefGlueNextAvaloniaRuntime.Shutdown`。
7. **TreatWarningsAsErrors=true**:缺注释警告 CS1591/CS1572/CS1573/CS1574/CS1711/CS1712/CS1734 在 `Directory.Build.props` 已抑制;新增注释规范后续统一补齐。
8. **版本单源**:`TaruiVersion=0.1.0` 在 `Directory.Build.props`,所有可打包项目与其一致;CI 校验等于 `@lytree/api` 的 `package.json` 版本。
9. **Lockstep 发布**:`Tarui.*` NuGet 与 `@lytree/api` npm lockstep 推进,变更须同步升级。
10. **零外部发布依赖**:CLI 零第三方依赖,仅 BCL + `System.Text.Json` 源生成。

---

## 4. 插件目录解剖

每个 `Tarui.Plugins.*` 项目保持同一形态:

```text
Tarui.Plugins.Foo/
  Tarui.Plugins.Foo.csproj   # ProjectReference Tarui.Ipc + Tarui.Contracts
  FooPlugin.cs               # : ITaruiPlugin, ConfigureCommands(builder)
  FooCommands.cs             # 静态 handler 方法
  FooScope.cs                # 可选:PathScope/快捷键 scope 解析
  FooJsonContext.cs          # 子 JsonSerializerContext(注册插件专属 DTO)
  InternalsVisibleTo("Tarui.Plugins.Tests")  # 仅集成测试可见 internal
```

对应 NuGet 包 `Tarui.Plugins.Foo` 的元数据由 `Directory.Build.props` 统一写入;不要在 `csproj` 里覆写 `Version`、`Authors`、`PackageId`、`GenerateDocumentationFile`、`IncludeSymbols`。

### 4.1 现有插件清单

| 插件包 | 命令前缀 | 命令数 | 备注 |
| --- | --- | --- | --- |
| `Tarui.Plugins.Core` | `core:app|*` | 1 | 壳握手 `get-info` |
| `Tarui.Plugins.Window` | `core:window|*` | 24 | 窗口生命周期、几何、装饰、监视器 |
| `Tarui.Plugins.Webview` | `plugin:webview|*` | 5 | 当前 Webview + 多 Webview 寻址 |
| `Tarui.Plugins.WindowState` | `plugin:window-state|*` | 3 | 持久化窗口尺寸/位置 |
| `Tarui.Plugins.Event` | `core:event|*` | 1 | `emit` |
| `Tarui.Plugins.Dialog` | `plugin:dialog|*` | 2 | 文件/目录选择器 |
| `Tarui.Plugins.System` | `core:path\|core:os\|core:process\|core:shell\|core:clipboard` | 7 | 平台能力 |
| `Tarui.Plugins.FileSystem` | `plugin:fs|*` | 10 | 受 `PathScope` 限制 |
| `Tarui.Plugins.Menu` | `plugin:menu|*` | 3 | 原生菜单 |
| `Tarui.Plugins.Tray` | `plugin:tray|*` | 6 | 系统托盘 |
| `Tarui.Plugins.Notification` | `plugin:notification|*` | 4 | 系统通知 |
| `Tarui.Plugins.Autostart` | `plugin:autostart|*` | 3 | 开机自启 |
| `Tarui.Plugins.GlobalShortcut` | `plugin:global-shortcut|*` | 4 | 系统级快捷键,带 scope allow/deny |
| `Tarui.Plugins.Store` | `plugin:store|*` | 6 | KV 持久化,scope 路径白名单 |
| `Tarui.Plugins.Log` | `plugin:log|*` | 1 | 日志落盘与前端监听 |
| `Tarui.Plugins.DeepLink` | `plugin:deep-link|*` | 2 | 自定义 scheme 路由 |
| `Tarui.Plugins.Updater` | `plugin:updater|*` | 2 | 检查与下载(签名校验 `latest.json` 待冻结) |

新增插件意味着:DTO record + `TaruiJsonContext` 注册 + `FooPlugin : ITaruiPlugin` + `AddFooPlugin()` 扩展 + capability 清单同步 + `@lytree/api/foo` TypeScript 模块 + 自测试 + Architecture gate 刷新。

---

## 5. 修改 Shell 或 Hosting 的步骤

1. **同步分层**:`Shell` 是 `Hosting` 的下层;`Hosting` 不能引用 `Shell`,`Shell` 反向可独立使用(`ShellBootstrap` 已被删除,改走 DI)。
2. **生命周期桥**:任何对 Avalonia `IClassicDesktopStyleApplicationLifetime` 的触碰必须经 `TaruiLifetimeBridge` + `HostShutdownWatcher`,避免破坏 Ctrl+C / 窗口关闭的桥接。
3. **窗口配置合并**:窗口默认值 < `Tarui:Window:*` 配置 < `builder.Window` 代码配置;数值/布尔用 `InvariantCulture` 解析,**非法值必须 fail fast**。
4. **能力校验位置**:新增命令必须经 `CommandRouterBuilder.Add(...)` 注册;`CommandRouter.RegisteredPermissions` 会自动收录该命令的 permission ID;任何手工登记都会被架构门禁视为可疑。
5. **架构门禁**:`Tarui.Architecture.Tests` 用 Roslyn 扫描 `src/` 下 active 文件,任何引入 `ActivatorUtilities`、`Assembly.Load*`、`GetAssemblies`、反射 JSON 路径都会被 CI 拒。

---

## 6. 修改 IPC 协议

IPC 是契约,**改动需三处同步**且保持向后兼容:

| 位置 | 文件 | 改动 |
| --- | --- | --- |
| 后端 DTO | `src/core/Tarui.Contracts/**` | 新增 record,实现 `ITaruiCommand<TArg,TRes>` 或事件 record |
| JSON 元数据 | 同上,加 `[JsonSerializable(typeof(...))]` 进 `TaruiJsonContext` | 必须 |
| 后端处理器 | `src/plugins/Tarui.Plugins.Foo/FooPlugin.cs` | `ConfigureCommands` 中 `commands.Add("plugin:foo|do", DoHandler, "plugin:foo|do")` |
| 前端桥接 | `web/packages/api/src/foo.ts` | 类型 + 调用包装 |
| 包导出 | `web/packages/api/package.json` | 新增 `"./foo"` 子路径 |
| 能力清单 | `examples/demo/capabilities/*.json` 或用户 app 的 `capabilities/*.json` | 加入 `plugin:foo|do` |

禁止:

- 在 `IpcDispatcher` 之外添加全局 IPC 入口(避免绕开 capability 校验)。
- 用反射序列化 DTO(无 `JsonSerializerContext` 标注)。
- 在前端用裸 `invoke('plugin:foo|do', ...)` 取代 `@lytree/api/foo`(失去类型)。

---

## 7. WebView / CefGlue 适配层开发

`src/webview/cefglue/` 是 **第三方源码**,只在以下情况修改:

- 上游 CEF 升级(目前锁定 `150.0.11+gb887805+chromium-150.0.7871.115`)。
- Avalonia 主版本升级(目前 12.1.1)。
- 移除上游反射组件(ObjectBinding、ReactiveUI、System.Reactive)早已完成。

任何对此目录的改动需在 PR 描述中注明原始上游 commit ID,避免本地修改漂移。

`Tarui.WebView.CefGlueNext` 是 Tarui 适配层:

- `AddCefGlueWebView()` 注册 `IpcDispatcher` ↔ `CefGlueNextAvaloniaWebView` 桥。
- `CefGlueNextWebAppOptions.FromConfiguration(IConfiguration)` 解析 `Tarui:Web:*` 与 `TARUI_WEB_*` 环境变量。
- `tarui://localhost` Scheme 模式走 `CefSchemeHandlerFactory`,要求 GET/HEAD、严格 origin 校验、文件大小上限、SPA fallback 仅对扩展名缺失的主帧导航开启。

---

## 8. 前端 SDK `@lytree/api` 开发

```
web/
  apps/Tarui.Web/         # React 业务应用(@lytree/web),用 workspace:* 引用 @lytree/api
  packages/api/           # @lytree/api,纯 ESM,提供 ipc/app/window/... 子路径
```

- TypeScript 6.0.x,严格 ESM,React 19,Vitest 3.2.4。
- `pnpm dev` / `pnpm build` / `pnpm lint` / `pnpm preview` 由 `web/package.json` 提供。
- 包名锁定 `@lytree/api`,版本必须与 `Directory.Build.props` 的 `TaruiVersion` 一致(CI `Version consistency` 步骤守护)。
- 命名约定:`openDialog`/`openExternal` 在 barrel 中区分(都叫 `open` 时);`fs`/`store`/`log` 走 namespace 风格;`window.getCurrent()` 无 label 时指向当前 webview。

新增模块步骤:

1. 在 `web/packages/api/src/<name>.ts` 创建模块并定义类型 + `invoke` 包装。
2. 在 `web/packages/api/src/index.ts` barrel 中导出(必要时重命名)。
3. 在 `web/packages/api/package.json` 的 `exports` 添加 `"./<name>"`。
5. 在 `web/packages/api/__tests__` 加 Vitest 单元测试(契约 stub 即可)。
6. `pnpm build` + `pnpm lint` 通过。

---

## 9. 模板与脚手架(`Tarui.Templates`)

模板项目 `src/templates/Tarui.Templates/` 提供 `tarui-app`:

```
.template.config/template.json   # dotnet new 元数据
MyApp.Desktop/                  # .NET 宿主(.csproj、Program.cs、appsettings.json、app.manifest)
web/                            # React 前端(package.json、vite.config.ts、src/)
capabilities/                   # 最小权限清单
```

升级模板(新增能力、改 deps):

1. 直接修改 `src/templates/Tarui.Templates/` 对应文件。
2. 重新构建:`dotnet build src/templates/Tarui.Templates/Tarui.Templates.csproj`。
3. 本地验证:`dotnet new tarui-app -n Test -o .out/Test` 后跑通 `dotnet run`。
4. 跑 CLI 自测试 `tests/Tarui.Cli.Tests`,确认解析不变。

---

## 10. 测试约定

仓库测试是 **控制台式自测试**,非 xUnit/NUnit。每一个 `tests/Tarui.*.Tests/`:

- `<Name>.Tests.csproj`(`OutputType=Exe`,`net10.0`)。
- `Program.cs` 内 `Main` 串联多个行为用例,使用 `RunCase("BehaviorsName", () => { ... })` 一类 helper,**断言失败必须抛带说明的异常**。
- 测试名用 `PascalCase` 行为描述,如 `DeniesCommandsOutsideCapability`、`RoutesWindowCloseRequestToWebview`。
- 受宿主环境约束的用例放 `.requires-env.txt`(每行一个 ENV 名),未设置时 `eng/test-all.ps1` 标记为 skipped,不计入基线。
- 命名空间使用文件范围,4 空格缩进,异步方法以 `Async` 结尾。

`eng/test-all.ps1 -BaselineCount 21`:

```powershell
# 发现 tests/*.Tests/*.Tests.csproj,按字母序 dotnet run;
# 任一失败立即停止;通过数 < 21 时抛错(防止"意外删除测试"回归)
./eng/test-all.ps1 -BaselineCount 21
```

`Tarui.Architecture.Tests` 独立运行,做禁反射/禁扫描/禁 `ActivatorUtilities`/JSON 源生成/CefGlue 包内容等的静态扫描。修改依赖或分层后必须跑通。

```powershell
dotnet run --project tests/Tarui.Architecture.Tests --no-build
```

---

## 11. CI / Release 工作流

`.github/workflows/ci.yml`(PR/branch 门禁):

1. `dotnet restore` + `dotnet build -c Release` 0 warnings。
2. `dotnet pack` 产出 `artifacts/nuget/*.nupkg` 与 `.snupkg`,校验存在。
3. `Tarui.Architecture.Tests` 对 `CefGlue.Next.Avalonia` 包做组件包内容门禁(`--require-package`)。
4. 外部 NuGet 消费者冒烟(还原 + 构建)。
5. 版本一致性:`Directory.Build.props` == `@lytree/api/package.json`。
6. `eng/test-all.ps1 -BaselineCount 21` 全量自测试。
7. `pnpm lint` + `pnpm build`。

`.github/workflows/release.yml`(tag `tarui-v<version>` 或 manual):

1. 校验 tag。
2. `dotnet pack` + `dotnet build src/tarui-cli`。
3. `tarui build --bundle zip,msix` 产 `examples/demo/dist/`。
4. 推 nuget(OIDC trusted publishing,需 `NUGET_USER` secret)。
5. 推 npm `@lytree/api`(OIDC provenance,需 `NPM_USER` 关联的 trusted-publisher 配置)。
6. 创建 GitHub Release,挂 `dist/*.zip|*.msix` + `artifacts/nuget/*.nupkg`。

MSIX 签名:`WINDOWS_CERT_*` secrets 可选,无证书时产未签名包。证书采购是分发前置项。

---

## 12. 本地提交流程

```powershell
# 0. 拉最新 master
git pull --rebase

# 1. 还原 + 构建(0 警告是硬指标)
dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln -c Release --no-restore

# 2. 跑自测试 + 架构门禁
./eng/test-all.ps1 -BaselineCount 21
dotnet run --project tests/Tarui.Architecture.Tests -c Release --no-build

# 3. 前端 lint + build
cd web
pnpm install --frozen-lockfile
pnpm lint
pnpm build
cd ..

# 4. commit(Conventional Commit:feat / refactor / fix / chore / docs / test)
git checkout -b codex/<topic>
git add -A
git commit -m "feat: <imperative summary>"
git push -u origin codex/<topic>

# 5. 开 PR,描述行为/架构影响、列出验证命令、关联 issue
```

提交规范:

- 一个 commit 一个目的,祈使句摘要。
- 触及契约、配置、工作流时同步更新 `README.md` / `docs/`。
- 可见 Web 或桌面 UI 变化需附截图。
- **Agent 协作**:拆分独立子任务,优先 Luna,其次 Terra,集成前审查所有委派结果。

---

## 13. 调试与诊断技巧

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

---

## 14. 进阶阅读

- [`architecture.md`](architecture.md) — 完整所有权边界、IPC 模型、生命周期顺序。
- [`hosting.md`](hosting.md) — `TaruiHost` 内部设计、配置键全表、Hosting/Shell 分层。
- [`dev-workflow-design.md`](dev-workflow-design.md) — W0~W5 实施计划、CLI/SDK/插件分发矩阵。
- [`tauri-desktop-alignment-plan.md`](tauri-desktop-alignment-plan.md) — 与 Tauri v2 桌面能力对齐的逐项交付记录。
- [`project-optimization-audit-2026-08-24.md`](project-optimization-audit-2026-08-24.md) — 基线审计与 P0~P2 优化项(尤其注意 P0-01 CI 分支门禁)。
