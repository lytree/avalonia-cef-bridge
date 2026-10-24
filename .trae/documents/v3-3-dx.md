# V3-3 DX 增强：CDP 消息通道 + 类型化命令契约

## Context（为什么做）

`tauri-v3-alignment.md` §4.4 定义 V3-3「开发体验增强」，四项中：webview 版本暴露已随 V3-0 完成、resourceDir 仅评估登记。本次交付两项（用户确认「全量」）：

- **D3 — CDP 消息通道**：Tarui 当前只暴露 `plugin:webview|devtools`（Show/Close），无法收发 Chrome DevTools Protocol 消息，性能分析 / 内存快照等工具链不可用。CEF 底层已具备 `CefBrowserHost.SendDevToolsMessage` + `CefDevToolsMessageObserver`，本项把它接通并 capability 门控（默认不授权）。
- **D11 — 类型化命令契约**：Roslyn 生成器今天只产出 `GeneratedCommandCatalog`（仅命令 ID 列表，**零消费方**），手写 `@lytree/api` 与命令契约之间无任何编译期/CI 校验。本项让生成器产出「命令 → 输入/输出 DTO」契约，并新增漂移门禁。

## 已核实前提

- 生成器作为 analyzer 挂到**每个插件工程**（如 `Tarui.Plugins.Webview.csproj:11` `<ProjectReference ... OutputItemType="Analyzer">`），故每个程序集各自生成一份 `Tarui.Generated.GeneratedCommandCatalog`（同名，跨程序集引用会 CS0433）。当前**无任何消费方**（全仓仅生成器产出）。
- `[Tarui.Command]`（`src/core/Tarui.Contracts/CommandAttribute.cs`）只带命令名；处理器签名 `ValueTask<TResult> X(TArgs options, CommandContext ctx, CancellationToken ct)` 使生成器可**从方法符号推断**输入（首参）与输出（`ValueTask<T>` 泛型实参），但看不到权限（权限在 `ConfigureCommands` 的 `commands.Add(..., permissionId)`）。
- `ITaruiAvaloniaWebView` 的 CEF 实现是**适配器** `CefGlueNextWebView`（`CefGlueNextWebViewFactory.cs:138`），它包住 `CefGlueNextAvaloniaWebView`（`ContentControl`，持有 `AvaloniaCefBrowser`）。CDP 须在两层同时落地。
- 浏览器句柄获取：`BaseCefBrowser.LifeSpanHandler`（`CefGlue.Common/BaseCefBrowser.cs:218`，public setter）→ 自定义 `LifeSpanHandler.OnAfterCreated(CefBrowser)` → `browser.GetHost()`（`CefGlue.Core/Classes.Proxies/CefBrowser.cs:27`）→ `CefBrowserHost.AddDevToolsMessageObserver(observer)`（`CefBrowserHost.cs:470`，返回 `CefRegistration`）；发送走 `SendDevToolsMessage(byte[])` 扩展（`CefBrowserHostExtensions.cs:7`）。**不改动 vendored CefGlue**。
- 事件分发：`WebviewSession` 订阅 webview 原生事件 → `EventRouter.EmitToWebviewAsync(webviewLabel, name, payload)`（`EventRouter.cs:51-79`）→ capability `events` 门控（`MayReceive`）。仿 `webview://render-process-gone` 即可。
- 运行时错误码为**大写**（`PERMISSION_DENIED`/`COMMAND_NOT_FOUND`/`COMMAND_FAILED`，`CommandRouter.cs:89-117`），前端回退亦大写（`ipc.ts:273`）；无 `NOT_SUPPORTED` → 需新增映射。

---

## Item A — CDP 消息通道（D3）

### A1 新契约接口（`src/webview/Tarui.WebView.Abstractions/ITaruiDevToolsProtocol.cs`）

引擎中立、**仅 CEF 实现**的可选能力接口（可选接口 + `is` 探测，非反射；不进通用 `ITaruiAvaloniaWebView`，遵守 D2「运行时特有表面隔离」）：

```csharp
public sealed class TaruiDevToolsMessageEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public interface ITaruiDevToolsProtocol
{
    /// <summary>Sends a CDP message; false when the browser is not ready yet.</summary>
    bool TrySendDevToolsMessage(string message);

    event EventHandler<TaruiDevToolsMessageEventArgs>? DevToolsMessageReceived;
}
```

### A2 CEF 底层实现（`src/webview/Tarui.WebView.CefGlueNext/CefGlueNextAvaloniaWebView.cs`）

- ctor：`_browser.LifeSpanHandler = _lifeSpanHandler`（自定义 `LifeSpanHandler` 子类；fork 的 `CommonCefLifeSpanHandler` 会链式调用它，弹窗行为不变）。
- `OnAfterCreated(CefBrowser browser)` → `AttachDevTools(browser)`：`_devToolsHost = browser.GetHost()`；`_devToolsObserver = new CefGlueNextAvaloniaDevToolsObserver(this)`；`_devToolsRegistration = _devToolsHost.AddDevToolsMessageObserver(_devToolsObserver)`。
- 观察者 `CefGlueNextAvaloniaDevToolsObserver : CefDevToolsMessageObserver`：`OnDevToolsMessage` 内 `Marshal.PtrToStringUTF8(message, size)`（**回调作用域内即刻拷贝**）→ `owner.RaiseDevToolsMessage(json)`；其余抽象成员空实现。
- `RaiseDevToolsMessage`：`Dispatcher.UIThread.CheckAccess()` 为真直接触发 `DevToolsMessageReceived`，否则 `Post`。
- `public bool TrySendDevToolsMessage(string message)`：先 `ArgumentException.ThrowIfNullOrWhiteSpace`（便于单测）→ UI 线程内 `_devToolsHost is null ? false : host.SendDevToolsMessage(Encoding.UTF8.GetBytes(message))`。
- 释放：`CloseBrowserCore()` 先 `_devToolsRegistration?.Dispose()`、置空 `_devToolsObserver/_devToolsHost`、`_browser.LifeSpanHandler = null`，再走原流程。

### A3 适配器（`CefGlueNextWebViewFactory.cs` 的 `CefGlueNextWebView`）

`CefGlueNextWebView : ITaruiAvaloniaWebView, ITaruiDevToolsProtocol, IAsyncDisposable`：转发 `TrySendDevToolsMessage` 与 `DevToolsMessageReceived`（缓存 delegate 字段，同现有事件模式），并纳入 `DetachComponentEvents/Dispose/DisposeAsync`。

### A4 DTO + 元数据（`src/core/Tarui.Contracts`）

- `WebViewContracts.cs`：`public sealed record WebviewDevToolsMessageOptions(string Message, string? Label = null);` 与 `public sealed record WebViewDevToolsMessageEvent(string Message);`
- `IpcContracts.cs`：为两者各加 `[JsonSerializable]`。

### A5 命令层

- `IWebviewService.cs`：`ValueTask<Unit> SendDevToolsMessageAsync(string webviewLabel, string message, CancellationToken ct);`
- `WebviewPlugin.cs`：`commands.Add("plugin:webview|send-devtools-message", WebviewDevToolsMessageOptions, Unit, handlers.SendDevToolsMessageAsync, "plugin:webview|send-devtools-message")`；处理器走 `Resolve(options.Label, context, "plugin:webview|send-devtools-message")`；把该权限加入 `OtherWebviewPermissions`（注册 `-other-webview` 变体）。

### A6 Shell 实现 + 错误码（`src/desktop/Tarui.Shell/AvaloniaWebviewService.cs`）

`SendDevToolsMessageAsync`：UI 线程内取 session → `session.WebView is not ITaruiDevToolsProtocol cdp → throw NotSupportedException("当前 webview 运行时未提供 CDP 通道")`；`cdp.TrySendDevToolsMessage(message)` 为 false 亦抛（消息区分「运行时无 CDP」与「浏览器未初始化」）。
`src/core/Tarui.Ipc/CommandRouter.cs`：在通用 `catch` 前加 `catch (NotSupportedException e) → InvokeResponse.Fail(id, "NOT_SUPPORTED", e.Message)`。

### A7 事件（`src/desktop/Tarui.Shell/WebviewSession.cs`）

常量 `webview://devtools-protocol`；ctor 内 `if (_webView is ITaruiDevToolsProtocol cdp) { _devToolsProtocol = cdp; cdp.DevToolsMessageReceived += OnDevToolsMessage; }`；处理器 `if (!MayReceive(DevToolsProtocolEvent)) return;` 后 `FireAndForget.Run(_eventRouter.EmitToWebviewAsync(WebviewLabel, DevToolsProtocolEvent, SerializeToElement(new WebViewDevToolsMessageEvent(json), TaruiJsonContext.Default.WebViewDevToolsMessageEvent)))`；`DetachWebViewEvents()` 解绑。

### A8 前端（`web/packages/api/webview.ts`）

```ts
export const DEVTOOLS_PROTOCOL_EVENT = 'webview://devtools-protocol'
export type DevtoolsProtocolMessage = { message: string }
async sendDevtoolsMessage(message: string): Promise<void>   // invoke('plugin:webview|send-devtools-message', { message, ...this.target() })
onDevtoolsProtocol(handler: (msg: DevtoolsProtocolMessage) => void): Unlisten
```

### A9 demo 能力清单

`examples/demo/capabilities/main.json` **不**添加该权限与事件（保持默认不授权），文档给示例片段。

### A10 测试

- `tests/Tarui.Shell.Tests`：`EmitsDevToolsProtocolEventWhenAuthorized`、`SuppressesDevToolsProtocolEventWithoutCapability`、`IgnoresDevToolsMessagesFromWebViewWithoutProtocol`（`FakeWebView` 增补 `ITaruiDevToolsProtocol` 或新增 `FakeDevToolsWebView`）。
- `tests/Tarui.Plugins.Tests`：`RegistersSendDevToolsMessageCommandAndOtherWebviewPermission`、`RoutesSendDevToolsMessageToService`、`DeniesSendDevToolsMessageWithoutPermission`（`FakeWebviewService` 增补方法 + `Calls`）。
- `tests/Tarui.Ipc.Tests`：`MapsNotSupportedExceptionToNotSupportedCode`。
- `tests/Tarui.WebView.Tests.CefGlueNext`：`RejectsEmptyDevToolsMessage`（空/空白消息抛）、`SendDevToolsMessageBeforeInitializationReturnsFalse`。（真机 CDP 收发路径无法在控制台自测，沿用 Phase 5 真机验收待办。）

---

## Item B — 类型化命令契约 + 漂移门禁（D11）

### B1 契约 DTO（`src/core/Tarui.Contracts`）

`public sealed record TaruiCommandContract(string Id, string Input, string Output);`（不含权限——权限由 `configureCommands` 的 `Add(..., permissionId)` 保留为运行时事实；v1 契约聚焦「命令 → 输入/输出 DTO」，`.d.ts` 全量生成按文档延后）。

### B2 生成器（`src/generators/Tarui.Ipc.Generators/TaruiCommandCatalogGenerator.cs`）

- `ReadCommand(ISymbol)` 改返回描述符：命令名（属性）+ 输入类型名（首参 `INamedTypeSymbol.Name`）+ 输出类型名（返回类型 `ValueTask<T>` 的 `T`）。输入/输出为 `int`/`double`/`string` 等基元或 `Unit` 时同样取符号名。
- 生成的类名改为**按程序集去重**（`{SanitizedAssemblyName}CommandCatalog`，如 `Tarui_Plugins_WebviewCommandCatalog`），避免多程序集同名 CS0433；同时保留 `Commands`（`IReadOnlyList<string>`）并新增 `Contracts`（`IReadOnlyList<TaruiCommandContract>`）。用 `.Combine(context.CompilationProvider)` 取 `Compilation.AssemblyName`。
- 新增诊断：输入/输出类型名必须在 `Tarui.Contracts.TaruiJsonContext` 上有同名静态属性，否则 `ReportDiagnostic` 警告（`TARUI1002`，仅警告以免破坏既有构建）——把「DTO 忘了进 JsonContext」变成编译期提醒。

### B3 漂移门禁（新 `tests/Tarui.Bindings.Tests`）

- csproj：引用 `Tarui.Contracts` + 全部插件工程（显式列表，无反射/无 DI）；`eng/test-all.ps1` 的 `*.Tests*.csproj` 发现规则自动纳入（29 套）。
- `Program.cs`（控制台式自测，行为命名方法）：
  - `FrontendInvokeLiteralsMatchGeneratedCommandContract()`：union 各程序集 `{Asm}CommandCatalog.Commands`，与 `web/packages/api/*.ts` 根目录下的 `invoke('…')` / `invoke<T>('…')` 字面量**双向**比对（屏蔽注释，排除 `__tests__`、`*.test.ts`）；复用 `tests/Tarui.Architecture.Tests` 的 `RepositoryRoot` 路径定位模式。
  - `InternalOnlyCommandsAreAllowlisted()`：白名单显式列出 C# 侧发起、前端无字面量的命令（如 `plugin:webview|eval-callback-complete`）。
  - `GeneratedCommandContractHasNoDuplicateIds()`。
- 文档同步：`docs/dev/testing.md` 增补该门禁说明。

---

## 验证

```powershell
dotnet build tarui.net.slnx --no-restore                 # 期望 0 警告 0 错误
pwsh -File eng/test-all.ps1 -Configuration Debug         # 期望 29/29 通过
dotnet run --project tests/Tarui.Bindings.Tests --no-build
pnpm -C web run lint && pnpm -C web --filter @lytree/api run build
```
负向演练：临时把 `web/packages/api/webview.ts` 里某命令字面量改错 → `Tarui.Bindings.Tests` 必须失败（改回）。

## 风险 / 延后

- **CDP 真机路径不可自测**：`LifeSpanHandler.OnAfterCreated` → `GetHost` → `AddDevToolsMessageObserver` 的运行时行为只能在真机验证；若 fork 的处理器未按预期链式委派，改用手持 `CefBrowser` 的其他钩子（实现时确认）。属既有 Phase 5 真机验收范围。
- **D2 `ICefWebviewExtension`**：本项以 `ITaruiDevToolsProtocol`（Abstractions 可选接口）满足 CDP；CEF 专有但无消费者的 `browser_runtime_style` 等预留位仍**不建**（避免为抽象而抽象），文档登记。
- **Item B v1 不含权限与 `.d.ts`**：如后续需要，可在 §4.4/B 基础上追加。
- 文档同步：`docs/design/tauri-v3-alignment.md`（§4.4 状态 + §2 表 D3/D11 勾选）、`docs/design/webview.md`（CDP 通道）、`docs/design/ipc.md`（`NOT_SUPPORTED` 错误码）、`docs/usage/frontend-bridge.md`（`sendDevtoolsMessage`/`onDevtoolsProtocol`）、`docs/usage/plugins.md`、`docs/dev/testing.md`（漂移门禁）。
