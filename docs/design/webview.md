# WebView 适配层

> 浏览器栈分层、`Tarui.WebView.CefGlueNext` 适配、CefGlue 内置源码边界。

## 1. 分层

浏览器栈刻意划分为四层:

| 层 | 职责 | 可引用的依赖 |
| --- | --- | --- |
| `Tarui.WebView.Abstractions` | UI 中立的导航、脚本、下载、文件拖放与拖拽区域契约 | 无 Avalonia,无 CefGlue |
| `Tarui.WebView.Avalonia` | 承载 Avalonia `Control` 的契约 | Avalonia + Tarui WebView 契约 |
| `Tarui.WebView.CefGlueNext` | 直接的 Avalonia 浏览器控件、CefGlue handler、运行时与原生浏览器生命周期 + Tarui 适配 | Avalonia + 内置 CefGlue + Tarui 契约(原 `CefGlue.Next.Avalonia` 已并入) |

依赖关系:

```text
Tarui.WebView.Abstractions
  ↑
Tarui.WebView.Avalonia
  ↑                                 ┌─ Tarui.WebView.CefGlueNext ─┐
Tarui.Shell ───────────────────────►│  CefGlueNextAvaloniaWebView │
Tarui.Hosting ─────────────────────►│  CefGlueNextAvaloniaRuntime │
                                    └──────────────────────────────┘
```

- `Tarui.WebView.Abstractions` 不得引用 Avalonia 或 Xilium CefGlue。
- `Tarui.Shell` / `Tarui.Hosting` 可使用 Avalonia,但不得引用 Xilium CefGlue。
- `src/webview/cefglue/*` 是 vendored implementation inputs,只被 `Tarui.WebView.CefGlueNext` 引用;不作为应用面对的包依赖。

## 2. CefGlue 内置源码边界

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

## 3. Tarui 适配层

`Tarui.WebView.CefGlueNext` 是 Tarui 适配层,提供:

- `AddCefGlueWebView(this IServiceCollection)` 注册 `IpcDispatcher` ↔ `CefGlueNextAvaloniaWebView` 桥,壳施加窗口 capability 与 IPC 策略。
- `CefGlueNextWebAppOptions.FromConfiguration(IConfiguration)` 解析 `Tarui:Web:*` 与 `TARUI_WEB_*` 环境变量。
- `tarui://localhost` Scheme 模式走 `CefSchemeHandlerFactory`,要求 GET/HEAD、严格 origin 校验、文件大小上限、SPA fallback 仅对扩展名缺失的主帧导航开启。
- `CefGlueNextAvaloniaRuntime.RunSubProcess(args)` / `Initialize(options)` / `Shutdown()` 管理 CEF 子进程派发与原生运行时生命周期。
- `CefGlueNextAvaloniaRuntimeOptions.UserAgent` / `ProxyServer` 把对应 CEF 命令行 / `CefSettings` 字段透传(仅初始化期有效)。
- `CefGlueCookieStore` 提供 `IWebViewCookieManager` 实现,无浏览器宿主时由插件层诚实降级。
- `IWebViewPermissionGuard` / `WindowPermissionGuard` 把 Capability 解析结果与导航 / 下载决策绑定。

直接使用 Avalonia 的应用:安装 `Tarui.WebView.CefGlueNext`,在 host 启动前调用 `CefGlueNextAvaloniaRuntime.RunSubProcess(args)`,初始化一份运行时配置,嵌入 `CefGlueNextAvaloniaWebView`,并在退出 Avalonia 消息循环前 await 所有 WebView 的关闭。随后应用停止并释放 Host,在 `Program` 的 `finally` 块中调用 `CefGlueNextAvaloniaRuntime.Shutdown()`。Tarui 应用通常使用 `Tarui.WebView.CefGlueNext.AddCefGlueWebView()` 扩展,以便 Shell 施加窗口 capability 与 IPC 策略。

## 4. 渲染作用域

Avalonia 12 移植当前支持原生窗口渲染(Native Windowed)。以下功能刻意排除直到 Avalonia 12 专用实现就位:

- OSR(Off-Screen Rendering)
- 共享帧投递 UI
- Avalonia 11 拖放适配器

## 5. 拖放与拖拽区域

真实 windowed CEF 上线后:

- 文件拖放三事件定向投递:`window://file-drop-entered` / `window://file-drop-left` / `window://file-dropped`(走 capability 授权)。
- `DraggableRegion` 命中检测 + `NoDrag` 覆盖(前端 HTML `data-tarui-drag-region` / `data-tarui-nodrag`)。
- 差异比较:`draggable region` 走 `DraggableRegion.SetRectanglesAsync`,拖放命中走 `INavigationRequest` + `IDownloadRequest` 决策。

## 6. 渲染进程与崩溃事件

`webview://render-process-gone` 事件在 CEF `OnRenderProcessTerminated` 触发,把 status 映射为 `crashed` / `killed` / `other`;该事件由 capability `events` 授权接收。

## 7. WebView 运行时配置

CEF 仅支持初始化期配置,运行期改值无效。配置通道:

| 键 | 环境变量 | 落地 |
| --- | --- | --- |
| `Tarui:Web:UserAgent` | `TARUI_WEB_USER_AGENT` | `CefSettings.UserAgent` |
| `Tarui:Web:ProxyServer` | `TARUI_WEB_PROXY_SERVER` | CEF `--proxy-server` 命令行开关 |
| `Tarui:Web:CachePath` | — | `CefSettings.CachePath` |

详见 [`web-resource-mode.md`](web-resource-mode.md)。
