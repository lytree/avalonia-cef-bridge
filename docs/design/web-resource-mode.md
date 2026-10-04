# Web 资源模式

> HTTP / Scheme 双模式、`TaruiAppOrigin`、Scheme 处理、CSP / SPA fallback。

## 1. 模式选择

`CefGlueNextWebAppOptions`(由 `Tarui:Web:*` 配置键 + `TARUI_WEB_*` 环境变量兜底构建)在 CEF 初始化之前选择以下两种模式之一:

- **HTTP**:导航到精确的 `http://` 或 `https://` origin,主要用于 Vite dev 或托管本地服务。
- **Scheme**:在浏览器与渲染进程中注册 `tarui://localhost`,通过 `CefSchemeHandlerFactory` 直接服务打包文件;无 HTTP listener 创建。

推断优先级:

1. `Tarui:Web:Mode` 显式值(若设置)
2. 否则若设置了 `TARUI_WEB_URL` 则选择 HTTP
3. 否则若有打包好的 Web 目录则选择 Scheme
4. 仅在没有打包资源时才回退到本地开发 HTTP URL

## 2. 开发模式(HTTP)

```powershell
$env:TARUI_WEB_MODE = "http"
$env:TARUI_WEB_URL = "http://127.0.0.1:5173"
cd web
pnpm dev
```

CEF 直连 Vite dev server,前端 HMR 天然可用,无需额外机制。`tarui dev` 把 `build.beforeDevCommand` + `TARUI_WEB_URL` 编排成单命令(详见 [`../usage/quickstart.md`](../usage/quickstart.md))。

## 3. 生产模式(Scheme)

```powershell
cd web
pnpm build
cd ..
$env:TARUI_WEB_MODE = "scheme"
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj
```

Scheme 模式服务 `tarui://localhost/index.html`;`tarui build` 把 `web/dist` 复制到发布输出。

### 3.1 可调参数

| 键 | 环境变量 | 说明 |
| --- | --- | --- |
| `Tarui:Web:Root` | `TARUI_WEB_ROOT` | 包含 `index.html` 的静态资源目录 |
| `Tarui:Web:Scheme` / `Host` | `TARUI_WEB_SCHEME` / `TARUI_WEB_HOST` | 自定义 origin(默认 `tarui` / `localhost`) |
| `Tarui:Web:SpaFallback` | `TARUI_WEB_SPA_FALLBACK` | 主帧扩展名缺失导航回退到 `index.html`(默认 `true`) |
| `Tarui:Web:Csp` | `TARUI_WEB_CSP` | 覆盖生产环境 Content-Security-Policy |
| `Tarui:Web:MaxAssetBytes` | `TARUI_WEB_MAX_ASSET_BYTES` | 静态资源体积上限,默认 64 MiB |
| `Tarui:Web:UserAgent` | `TARUI_WEB_USER_AGENT` | 自定义 User-Agent(CEF 仅初始化期有效) |
| `Tarui:Web:ProxyServer` | `TARUI_WEB_PROXY_SERVER` | CEF `--proxy-server` 代理(仅初始化期有效) |
| `Tarui:Web:CachePath` | — | `CefSettings.CachePath`,缺省走 `TaruiApplicationIdentity` 派生的本地缓存目录 |

## 4. Scheme 处理安全规则

`CefSchemeHandlerFactory` 强制以下规则(单测 `Tarui.WebView.Tests` 覆盖):

- **方法**:仅接受 `GET` / `HEAD`;其他方法 405。
- **Origin 校验**:精确比对 scheme + host + 无 userinfo / port / traversal encoding / 控制字符 / colon / device paths / reparse points。
- **大小上限**:`MaxAssetBytes`(默认 64 MiB),超出 413。
- **MIME**:严格 `MimeMapping`,无 Sniffing。
- **CSP**:强制应用 `Content-Security-Policy` 头,除非显式覆盖。
- **SPA fallback**:仅扩展名缺失的主帧导航 404 时回退到 `index.html`;静态资源 miss 仍 404。
- **注册失败**:致命错,终止启动(非 fail-soft)。

## 5. 多 Scheme 共存

HTTP 与自定义 app scheme 可以共存:当配置了 content root(`Tarui:Web:Root` 或打包资产),HTTP 模式也会注册无端口的自定义 scheme(如 `tarui://localhost/`),从而允许窗口与 WebView 同时加载远程 HTTP 内容与本地资产。

默认导航策略放行所有应用 origin —— HTTP 起始 origin、自定义 scheme origin 与本地 dev server。`TaruiAppOrigin.AllowedSchemes` / `SchemeOrigin` 暴露可接受的 scheme 以便校验:

```csharp
public sealed record TaruiAppOrigin(
    Uri StartUri,
    IReadOnlyList<string> AllowedSchemes,
    Uri? SchemeOrigin);
```

## 6. Content root 探测

`FindContentRoot` 优先级:

1. 显式配置 `Tarui:Web:Root`
2. 环境变量 `TARUI_WEB_ROOT`
3. `AppContext.BaseDirectory` 内的 `web/dist/index.html`(Scheme 模式构建产物)
4. `web/apps/Tarui.Web/dist/index.html`(仓库内开发)

## 7. CSP 模板

Scheme 模式下默认 CSP(可被 `Tarui:Web:Csp` 覆盖):

```
default-src 'self';
script-src 'self';
style-src 'self' 'unsafe-inline';
img-src 'self' data:;
font-src 'self' data:;
connect-src 'self' ipc: http://ipc.localhost;
```

`connect-src` 包含 IPC 自定义协议与 `http://ipc.localhost`(CEF 自定义 scheme + HTTP hybrid 模式)。生产环境应显式收紧 `connect-src` 限定业务后端 origin。
