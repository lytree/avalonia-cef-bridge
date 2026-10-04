# WebView 资源模式(应用开发者视角)

> HTTP / Scheme 双模式、CSP、SPA fallback、运行时配置。
>
> 配套文档:[`../design/web-resource-mode.md`](../design/web-resource-mode.md)、[`./configuration.md`](configuration.md)。

## 1. HTTP 模式(开发)

```powershell
$env:TARUI_WEB_MODE = "http"
$env:TARUI_WEB_URL = "http://127.0.0.1:5173"
cd web
pnpm dev
```

或使用 `tarui dev`(CLI 编排):

```powershell
tarui dev    # 自动跑 beforeDevCommand + dotnet watch run,注入 TARUI_WEB_MODE / TARUI_WEB_URL
```

CEF 直连 Vite dev server,前端 HMR 天然可用。

## 2. Scheme 模式(生产)

```powershell
cd web
pnpm build
cd ..
$env:TARUI_WEB_MODE = "scheme"
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj
```

Scheme 模式服务 `tarui://localhost/index.html`;`tarui build` 把 `web/dist` 复制到发布输出。

## 3. 资源模式参数

可用 `Tarui:Web:*` 配置键或等价环境变量覆盖默认值:

| 配置键 | 环境变量 | 说明 |
| --- | --- | --- |
| `Tarui:Web:Mode` | `TARUI_WEB_MODE` | `http` / `scheme`(自动推断) |
| `Tarui:Web:Url` | `TARUI_WEB_URL` | 仅 HTTP 模式 |
| `Tarui:Web:Root` | `TARUI_WEB_ROOT` | 包含 `index.html` 的静态资源目录 |
| `Tarui:Web:Scheme` | `TARUI_WEB_SCHEME` | 自定义 scheme 名 |
| `Tarui:Web:Host` | `TARUI_WEB_HOST` | 自定义 host |
| `Tarui:Web:SpaFallback` | `TARUI_WEB_SPA_FALLBACK` | 默认 `true`;关闭后扩展名缺失的主帧导航直接 404 |
| `Tarui:Web:Csp` | `TARUI_WEB_CSP` | 覆盖生产环境 CSP |
| `Tarui:Web:MaxAssetBytes` | `TARUI_WEB_MAX_ASSET_BYTES` | 资源体积上限,默认 64 MiB |
| `Tarui:Web:UserAgent` | `TARUI_WEB_USER_AGENT` | CEF `UserAgent`(仅初始化期) |
| `Tarui:Web:ProxyServer` | `TARUI_WEB_PROXY_SERVER` | CEF `--proxy-server`(仅初始化期) |

未显式指定模式时,若设置了 `TARUI_WEB_URL` 则选择 HTTP;否则若有打包好的 Web 目录则选择 Scheme,仅在没有打包资源时才回退到本地开发 HTTP URL。

## 4. CSP 模板

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

## 5. SPA fallback

主帧扩展名缺失导航(如 `/users/123` 而非 `/users/123.html`)会自动回退到 `index.html`。资源路径(如 `/assets/logo.png`)miss 仍 404。

通过 `Tarui:Web:SpaFallback=false` 关闭回退。

## 6. 多 Scheme 共存

HTTP 与自定义 app scheme 可以共存:当配置了 content root(`Tarui:Web:Root` 或打包资产),HTTP 模式也会注册无端口的自定义 scheme(如 `tarui://localhost/`),从而允许窗口与 WebView 同时加载远程 HTTP 内容与本地资产。

默认导航策略放行所有应用 origin —— HTTP 起始 origin、自定义 scheme origin 与本地 dev server。

## 7. 导航策略

```json
{
  "Tarui": {
    "Web": {
      "Policy": {
        "NavExternal": "https:*"
      }
    }
  }
}
```

`NavExternal` 控制主帧之外允许导航的 origin(`https:*` / 自定义 glob)。

## 8. 开发 vs 生产差异

| 维度 | HTTP(开发) | Scheme(生产) |
| --- | --- | --- |
| 资源来源 | Vite dev server | `tarui://localhost/` 注册到本地 |
| HMR | 天然支持 | 不适用 |
| CSP | 浏览器默认 | 强制应用 CSP |
| 代理 | Vite 默认 | 走 `Tarui:Web:ProxyServer` |
| 资源大小 | 不限 | `MaxAssetBytes` 限制 |
| 端口 | 5173(Vite) | 无端口 |
