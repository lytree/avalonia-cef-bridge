# V3-1 多 WebView 改造：窗口/WebView 实体分离与挂载能力

## Context（为什么做）

`gap-analysis.md` §4 P0-0 与本仓库 `tauri-v3-alignment.md` §4.2 一致认定：**单窗口多 WebView 是当前唯一的架构级缺口**。Tarui 从 `WindowOptions` 到 `IWebViewFactory` 是「窗口 ↔ WebView 强绑定 1:1」模型，导致「主窗口 + 侧边浮层 webview」「标签页多视图」「托盘气泡窗」等形态必须绕开框架自建。

本次按用户决定**一次交付方案 §4.2 的两步**：
- **Step 1 实体分离**（无行为变化重构）：引入 `WebviewRegistry`，让 webview 成为可独立寻址的实体；旧 1:1 成为「单子视图」特例。
- **Step 2 挂载能力**：`plugin:webview|create` / `set-bounds` / `destroy`，在既有窗口内按 bounds 挂载多个 CEF surface。

已核实的有利前提（大幅降低工作量）：`CommandContext` 已同时携带 `WindowLabel` 与 `WebViewLabel`（当前恒等）；`WebviewSession` 已是 UI 中立逻辑节点；`ShellWindow` 已是纯原生窗口；`ShellWindowComposition.Content` 已是 `Panel`；扩展层只用 Docks/Chrome/Overlay，**唯一向 `Content` 添加子控件的是 `ShellWindow.AddWebview`**。

## 关键文件（现状）

- `src/desktop/Tarui.Shell/WindowRegistry.cs` —— `Entry{Window, Context, Sink, Webview?}`（单槽），实现 `IWindowSinkRegistry`
- `src/desktop/Tarui.Shell/WebviewAttacher.cs` —— `Attach()` 组装窗口+webview；`HandleWindowClosedAsync()` 释放
- `src/desktop/Tarui.Shell/WebviewSession.cs` —— `Label => Context.WindowLabel`；发射 `window://` / `webview://` 事件
- `src/desktop/Tarui.Shell/EventRouter.cs` —— `EmitToWindowAsync`/`EmitToAllAsync`/`EmitToWebviewAsync`（后者当前转发给前者）
- `src/desktop/Tarui.Shell/AvaloniaWebviewService.cs` —— 经 `registry.Get(label).Webview` 解析会话
- `src/desktop/Tarui.Shell/ShellWindow.cs` / `WebviewPresenter.cs` / `ShellWindowComposition.cs`
- `src/desktop/Tarui.Shell/TaruiShellServiceCollectionExtensions.cs` —— shell DI
- `src/plugins/Tarui.Plugins.Webview/{IWebviewService,WebviewPlugin,WebviewPermissionGuard}.cs`
- `src/core/Tarui.Contracts/{WebViewContracts,IpcContracts}.cs` —— DTO + `TaruiJsonContext`
- `src/core/Tarui.Ipc/CommandContext.cs` / `CapabilitySet.cs`
- `web/packages/api/webview.ts` —— 前端桥接
- `examples/demo/capabilities/main.json` —— demo 授权清单

---

## Step 1 —— 实体分离（无行为变化）

### 1.1 新建 `src/desktop/Tarui.Shell/WebviewRegistry.cs`

```csharp
public interface IWebviewSinkRegistry
{
    IReadOnlyCollection<string> WebviewLabels { get; }
    bool TryGetSink(string webviewLabel, out IEventSink sink);
    bool TryGetCapabilities(string webviewLabel, out CapabilitySet capabilities);
    IReadOnlyList<string> WebviewLabelsFor(string windowLabel);
}

public sealed class WebviewEntry
{
    public WebviewEntry(string webviewLabel, string windowLabel, WebviewSession session, WebviewPresenter presenter);
    public string WebviewLabel { get; }
    public string WindowLabel { get; }
    public WebviewSession Session { get; }
    public WebviewPresenter Presenter { get; }
    public CommandContext Context => Session.Context;
}

public sealed class WebviewRegistry : IWebviewSinkRegistry
{
    public void Add(WebviewEntry entry);                 // 重复 label → InvalidOperationException
    public WebviewEntry Get(string webviewLabel);        // 缺失 → KeyNotFoundException
    public bool TryGet(string webviewLabel, out WebviewEntry entry);
    public bool Remove(string webviewLabel);
    public IReadOnlyList<WebviewEntry> RemoveWindow(string windowLabel); // 摘除并返回该窗口全部条目
}
```
- 主索引 `webview label → WebviewEntry`；副索引 `window label → webview label 列表`。
- 沿用 `WindowRegistry` 的 `Dictionary` + `object _gate` + 数组快照风格。
- 不变量：一个 webview 只属于一个窗口。

### 1.2 新建 `src/desktop/Tarui.Shell/WindowEventFanout.cs`

窗口级 `IEventSink`，把 `window://` 事件转发给该窗口的全部 webview（Step 1 恒为 1 个 → 行为不变）：

```csharp
internal sealed class WindowEventFanout(string windowLabel, IWebviewSinkRegistry webviews) : IEventSink
{
    public async ValueTask SendEventAsync(string eventName, JsonElement payload, CancellationToken ct = default)
    {
        foreach (var label in webviews.WebviewLabelsFor(windowLabel)) // 快照；迭代中摘除自动跳过
            if (webviews.TryGetSink(label, out var sink))
                await sink.SendEventAsync(eventName, payload, ct);    // 顺序 await，保序
    }
}
```
- 不在 fanout 内做 capability 过滤（`EventRouter.MayReceive` 已按窗口过滤，1:1 时与今天完全一致）。

### 1.3 改 `WindowRegistry.cs`
- 删除 `Entry.Webview` 单槽及其 XML 注释；`Entry.Sink` 注释改为「窗口级 fan-out 汇聚点」。
- `IWindowSinkRegistry` **签名不变**（因此 `EventRouter`、`Tarui.SingleInstance` 等零改动）。

### 1.4 改 `WebviewSession.cs`
- 删除 `Label`，改为 `public string WebviewLabel => Context.WebViewLabel;` 与 `public string WindowLabel => Context.WindowLabel;`。
- 发射点：file-drop 三处 → `EmitToWindowAsync(WindowLabel, …)`；download/navigation/render-process-gone 三处 → `EmitToWebviewAsync(WebviewLabel, …)`。Step 1 二者恒等 → 字节级行为不变。

### 1.5 改 `WebviewAttacher.cs`
- ctor 增加 `WebviewRegistry webviews`。
- `Attach(...)`：`Sink` 改为 `new WindowEventFanout(options.Label, _webviews)`；`entry` 不再带 `Webview`；构建 session/presenter 后 `_webviews.Add(new WebviewEntry(options.Label, options.Label, session, presenter))`（在 `WireWindowEvents` 之前注册）。
- `HandleWindowClosedAsync(...)`：`_registry.Remove(label)` → `var ws = _webviews.RemoveWindow(label)` → `await DisposeWebviewsAsync(ws)` → `NotifyWindowClosed` → `EmitToAllAsync("window://destroyed")`（顺序与今天一致）。
- 新增 `internal static ValueTask DisposeWebviewsAsync(IReadOnlyList<WebviewEntry>)`（`WebviewSession` 的 Interlocked 保证幂等），沿用既有 `CloseExtensionsAsync` 的「internal static 便于自测」模式。

### 1.6 改 `AvaloniaWebviewService.cs`
- ctor 增加 `WebviewRegistry webviews`（保留 `WindowRegistry`）。
- 四处 `registry.Get(label).Webview` → `webviews.Get(label).Session`；`GetStateAsync` 的 windowLabel 取 `webview.WindowLabel`、标题取 `windows.TryGet(windowLabel, …).Window.Title`。
- `ListAsync` → `webviews.WebviewLabels.ToArray()`。

### 1.7 改 `TaruiShellServiceCollectionExtensions.cs`
- `WindowRegistry` 之后追加 `.AddSingleton<WebviewRegistry>()` 与 `.AddSingleton<IWebviewSinkRegistry>(sp => sp.GetRequiredService<WebviewRegistry>())`。
- `IWebviewService` 工厂追加 `WebviewRegistry` 参数。

### 1.8 Step 1 测试（`tests/Tarui.Shell.Tests`）
- 改写 `EntryCarriesWebviewSessionAsSink`（断言 `entry.Sink == session` 已与生产不符）→ `WindowEntryHoldsWindowFanoutSink`。
- 新增：`WebviewRegistryIndexesByWebviewAndWindowLabel`、`WebviewRegistryRemoveWindowDetachesEveryHostedWebview`、`WindowFanoutDeliversWindowEventsToEveryHostedWebview`、`WindowFanoutWithoutWebviewsIsNoOp`、`WebviewSessionExposesSeparateWindowAndWebviewLabels`、`ClosingWindowDisposesEveryHostedWebview`、`WebviewRegistryCapabilitiesFollowSessionContext`。
- 扩展 `AddTaruiShellRegistersShellServices`：断言 `WebviewRegistry`/`IWebviewSinkRegistry` 可解析且为同一实例。
- `IWindowSinkRegistry`/`EventRouter` 签名不变 → `Tarui.Capabilities.Tests` / `Tarui.SingleInstance.Tests` / `Tarui.Plugins.Tests` / 全部 `Router*` 测试不受影响。

---

## Step 2 —— 挂载能力（bounds + 多 surface）

### 2.1 新增 DTO（`src/core/Tarui.Contracts/WebViewContracts.cs`）

```csharp
/// <summary>Position and size of a web view surface inside its host window (logical units).</summary>
public sealed record WebviewBounds(double X, double Y, double Width, double Height);

/// <summary>Creates an additional web view inside an existing window.</summary>
public sealed record WebviewCreateOptions(
    string Label,                // new web view label (unique)
    string? WindowLabel = null,  // host window; defaults to the caller's window
    string? Url = null,
    WebviewBounds? Bounds = null); // null = fill the host window

/// <summary>Repositions/resizes a web view surface; null bounds restore fill.</summary>
public sealed record WebviewSetBoundsOptions(WebviewBounds? Bounds, string? Label = null);
```
- 在 `TaruiJsonContext`（`IpcContracts.cs`）追加 `[JsonSerializable]`：`WebviewBounds` / `WebviewCreateOptions` / `WebviewSetBoundsOptions`。

### 2.2 布局：`WebviewSurfaceHost`（`src/desktop/Tarui.Shell/`）

`ShellWindowComposition.Content` 由 `new Panel()` 改为 `new WebviewSurfaceHost()`（仍声明为 `Panel`，兼容既有断言 `Content.Children.Count == 0`）：

```csharp
/// <summary>A panel that fills web views without bounds and positions bounded ones at their rect.</summary>
internal sealed class WebviewSurfaceHost : Panel
{
    protected override Size MeasureOverride(Size available) { /* 有 bounds 按 bounds 尺寸；否则 available */ }
    protected override Size ArrangeOverride(Size final)
    {
        foreach (var child in Children)
            child.Arrange(child is WebviewPresenter { Bounds: { } b }
                ? new Rect(b.X, b.Y, b.Width, b.Height)
                : new Rect(final));
        return final;
    }
}
```
- `WebviewPresenter` 增加 `WebviewBounds? Bounds { get; private set; }` + `SetBounds(WebviewBounds?)`（更新后 `InvalidateArrange()`）。
- `ShellWindow` 增加 `RemoveWebview(WebviewPresenter)`；`AddWebview` 保持（子控件默认 fill）。

### 2.3 `WebviewAttacher` 挂载 API

抽出私有 `CreateSession(windowLabel, webviewLabel, source)`（复用 Step 1 的 session 构造），新增：

```csharp
public WebviewEntry AttachWebview(ShellWindow window, string windowLabel, string webviewLabel, Uri source, WebviewBounds? bounds)
{
    var entry = _registry.Get(windowLabel);
    var context = new CommandContext(windowLabel, webviewLabel, entry.Context.Capabilities, _scopeOverlay);
    var (session, presenter) = CreateSession(window, context, source);   // 内部 + WireWebviewEvents
    window.AddWebview(presenter);
    if (bounds is not null) presenter.SetBounds(bounds);
    var webview = new WebviewEntry(webviewLabel, windowLabel, session, presenter);
    _webviews.Add(webview);
    return webview;
}

public async ValueTask DestroyWebviewAsync(string webviewLabel)   // UI 线程：摘 presenter + dispose session
```

### 2.4 `IWebviewService` 扩展 + `AvaloniaWebviewService` 实现

`src/plugins/Tarui.Plugins.Webview/IWebviewService.cs` 追加（保持 label 签名，context/guard 在插件层）：

```csharp
ValueTask<WebviewStateInfo> CreateAsync(WebviewCreateOptions options, CancellationToken cancellationToken);
ValueTask<Unit> SetBoundsAsync(string webviewLabel, WebviewBounds? bounds, CancellationToken cancellationToken);
ValueTask<Unit> DestroyAsync(string webviewLabel, CancellationToken cancellationToken);
```
`AvaloniaWebviewService`：ctor 增加 `WebviewAttacher attacher`；`CreateAsync` 在 UI 线程解析目标窗口 → `attacher.AttachWebview(...)`；`SetBoundsAsync` → `webviews.Get(label).Presenter.SetBounds(...)`；`DestroyAsync` → `attacher.DestroyWebviewAsync(...)`。

### 2.5 命令 + 权限（`WebviewPlugin.cs`）

- 命令：`plugin:webview|create` / `plugin:webview|set-bounds` / `plugin:webview|destroy`。
- `create` 的目标窗口 = `options.WindowLabel ?? context.WindowLabel`；**跨窗口**（≠ caller 窗口）需 `plugin:webview|create-other-window`（新增 `WebviewPermissionGuard.EnsureCreateTarget`）。
- `set-bounds` / `destroy` 复用 `EnsureOwnOrOtherWebview`（跨 webview 需 `plugin:webview|set-bounds-other-webview` / `plugin:webview|destroy-other-webview`）。
- 在 `OtherWebviewPermissions` 数组加入 `set-bounds` / `destroy`，并注册 `create-other-window` 等权限 ID（保持 capability 校验严格）。

### 2.6 前端（`web/packages/api/webview.ts`）

```ts
export type WebviewBounds = { x: number; y: number; width: number; height: number }
export type WebviewCreateOptions = { label: string; windowLabel?: string; url?: string; bounds?: WebviewBounds }
static async create(options): Promise<WebviewState>   // invoke('plugin:webview|create', ...)
async setBounds(bounds: WebviewBounds | null): Promise<void>
async destroy(): Promise<void>
```

### 2.7 demo 能力清单（`examples/demo/capabilities/main.json`）

在 permissions 追加：`plugin:webview|create`、`plugin:webview|set-bounds`、`plugin:webview|destroy` 及其 `-other-webview` 变体与 `plugin:webview|create-other-window`（如 demo 需要跨窗；否则仅 own 三项）。

### 2.8 Step 2 测试
- `tests/Tarui.Plugins.Tests`：`FakeWebviewService` 增补 `CreateAsync/SetBoundsAsync/DestroyAsync`；`RegistersWebviewCommandsWithPermissions` 的 `expected` 加入三条新命令；新增 `CreatesWebviewInsideCallerWindow`、`DeniesCrossWindowCreateWithoutPermission`、`SetsAndClearsWebviewBounds`、`DestroysWebviewReleasesSurface`。
- `tests/Tarui.Shell.Tests`：新增 `ShellWindowPositionsBoundedPresenterAndFillsUnbounded`、`AttachWebviewRegistersInWebviewRegistry`。
- `tests/Tarui.Capabilities.Tests`：若断言 demo 权限集合则同步新增条目。

---

## 验证

```powershell
dotnet build tarui.net.slnx --no-restore                     # 期望 0 警告 0 错误
pwsh -File eng/test-all.ps1 -Configuration Debug             # 期望 28/28 通过
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj   # AddCefGlueWebView 用法零破坏冒烟
pnpm -C web run lint                                         # 前端桥接
```
架构门禁关注点：`Tarui.Shell` 仍只依赖 `Tarui.WebView.Abstractions`（新类型不得引用 `CefGlueNext`/Xilium）；不得引入反射/程序集扫描。

## 风险与后续

- **Z-order / 多原生窗口叠加**：多个 windowed CEF `NativeControlHost` 同窗叠加的最小化/焦点行为需在真机冒烟（不在自测试范围）。
- **capability webview 维度**：本次 webview 继承宿主窗口 profile；后续若引入 `capabilities/*.json` 的 `webviews[]` 字段，`CapabilityLoader.KnownRootFields` 需显式扩展（严格校验防拼写漂移）。
- **文档同步**：`docs/design/tauri-v3-alignment.md` §4.2 两步勾选、`docs/design/webview.md` 分层图补 `WebviewRegistry`/`WindowEventFanout`、`docs/dev/codebase-map.md` 类型清单刷新、`docs/usage/plugins.md` webview 命令表补三条、`docs/usage/frontend-bridge.md` 补 `Webview.create/setBounds/destroy`。
