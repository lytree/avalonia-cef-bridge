# WebView / CefGlue 适配层开发

> `Tarui.WebView.CefGlueNext` 的适配、CefGlue 内置源码边界、修改触发条件。

## 1. 内置 CefGlue 源码边界

`src/webview/cefglue/` 是 **第三方源码**,只在以下情况修改:

- 上游 CEF 升级(目前锁定 `150.0.11+gb887805+chromium-150.0.7871.115`)。
- Avalonia 主版本升级(目前 `12.1.1`)。
- 移除上游反射组件(ObjectBinding、ReactiveUI、System.Reactive)早已完成。

任何对此目录的改动需在 PR 描述中注明原始上游 commit ID(目前基线 `e3389315dad795374be1a1e52c42d4e49cb6fe7b`),避免本地修改漂移。

上游反射组件移除清单:

- ~~ObjectBinding~~(基于 `MethodInfo.Invoke` / `Activator`)
- ~~泛型 JavaScript 求值~~(反射 + `JsonElement` 装箱)
- ~~ReactiveUI~~ + ~~System.Reactive~~

Tarui IPC 改为固定的 `window.invokeCSharpAction(json)` CEF process message 桥,所有 DTO 走 `JsonSerializerContext`。

## 2. Tarui 适配层职责

`Tarui.WebView.CefGlueNext` 是 Tarui 适配层:

- `AddCefGlueWebView()` 注册 `IpcDispatcher` ↔ `CefGlueNextAvaloniaWebView` 桥。
- `CefGlueNextWebAppOptions.FromConfiguration(IConfiguration)` 解析 `Tarui:Web:*` 与 `TARUI_WEB_*` 环境变量。
- `tarui://localhost` Scheme 模式走 `CefSchemeHandlerFactory`,要求 GET/HEAD、严格 origin 校验、文件大小上限、SPA fallback 仅对扩展名缺失的主帧导航开启。
- `CefGlueNextAvaloniaRuntime.RunSubProcess(args)` / `Initialize(options)` / `Shutdown()` 管理 CEF 子进程派发与原生运行时生命周期。
- `CefGlueNextAvaloniaRuntimeOptions.UserAgent` / `ProxyServer` 把对应 CEF 命令行 / `CefSettings` 字段透传(仅初始化期有效)。
- `CefGlueCookieStore` 提供 `IWebViewCookieManager` 实现,无浏览器宿主时由插件层诚实降级。
- `IWebViewPermissionGuard` / `WindowPermissionGuard` 把 Capability 解析结果与导航 / 下载决策绑定。

## 3. 直接使用 Avalonia 的应用

应用可以跳过 Tarui 适配层,直接安装 `Tarui.WebView.CefGlueNext` 并把 `CefGlueNextAvaloniaWebView` 放入视觉树:

```csharp
using Tarui.WebView.CefGlueNext;

if (CefGlueNextAvaloniaRuntime.RunSubProcess(args)) return;

CefGlueNextAvaloniaRuntime.Initialize(new CefGlueNextAvaloniaRuntimeOptions
{
    UserAgent = "MyApp/1.0",
    // ...
});

try
{
    var app = AppBuilder.Configure(() => new MyAvaloniaApp())
        .UsePlatformDetect()
        .StartWithClassicDesktopLifetime(args);
}
finally
{
    CefGlueNextAvaloniaRuntime.Shutdown();
}
```

Tarui 应用通常使用 `Tarui.WebView.CefGlueNext.AddCefGlueWebView()` 扩展,以便 Shell 施加窗口 capability 与 IPC 策略。

## 4. 修改 WebView 行为的步骤

1. **确认是不是上游问题**:CEF 行为异常先查上游 issue tracker,而非本地修改。
2. **走 Tarui 适配层**:所有 Tarui 侧行为(Capability / IPC / 事件路由 / Channel)都在 `Tarui.WebView.CefGlueNext` 实现,不污染 `src/webview/cefglue/`。
3. **新增 Capability 授权**:如 `plugin:webview|devtools`、`plugin:webview|eval-with-callback` 走 capability 闸门,与命令同构。
4. **新增事件**:`window://file-drop-*`、`webview://download-requested`、`webview://render-process-gone` 等通过 `EventRouter` 按 capability `events` 授权。
5. **拖放与拖拽区域**:`DraggableRegion` 命中检测 + `NoDrag` 覆盖(前端 HTML `data-tarui-drag-region` / `data-tarui-nodrag`)。

## 5. 自测试

`tests/Tarui.WebView.Tests` 覆盖:

- `CefSchemeHandlerFactory` 安全规则(GET/HEAD、origin 校验、size 限制、SPA fallback)
- `TaruiAppOrigin.AllowedSchemes` / `SchemeOrigin` 解析
- `CefGlueNextWebAppOptions.FromConfiguration` 配置键全表
- Web 资源模式选择逻辑(HTTP vs Scheme)

`tests/Tarui.WebView.Abstractions.Tests` 覆盖 UI 中立契约(navigation / download policy / drag region)。

## 6. CEF 升级流程(若上游发布新版本)

1. 上游同步:`git subtree pull --prefix=src/webview/cefglue <cef-glue-fork> main --squash`(若 fork 仓库);或本地 cherry-pick 上游 commit。
2. 验证 CEF Native 与 Avalonia 12 版本兼容性。
3. `tests/Tarui.Architecture.Tests` 必须仍 0 警告通过(reflection / ReactiveUI 移除约束)。
4. 在 PR 描述中明确上游 commit ID 与偏移 commit。
5. 升级后必须重跑全部自测试与 macOS 真机门禁。
