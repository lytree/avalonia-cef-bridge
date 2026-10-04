# 运行时配置:`appsettings.json`

> 宿主运行时配置:`Tarui:Window:*` / `Tarui:Web:*` / `Tarui:Application` / `Tarui:Notification` 等键全表。
>
> 配套文档:[`./webview-resource-mode.md`](webview-resource-mode.md)、[`../design/hosting.md`](../design/hosting.md)。

## 1. 优先级与加载

模板生成的 `appsettings.json` 是宿主运行时配置,优先级:**默认值 < `Tarui:Window:*` 配置 < `builder.Window` 代码配置**。

加载机制(由 `Tarui.Hosting` 内的 `HostApplicationBuilder` 驱动):

- 默认 `appsettings.json` —— `AppContext.BaseDirectory`
- `appsettings.{Environment}.json` —— `DOTNET_ENVIRONMENT` 决定
- 环境变量(`Tarui:Window:Title` / `TARUI_WEB_MODE` 等)
- 命令行参数(以 `--key=value` / `--key value` 形式)

数值/布尔用 `InvariantCulture` 解析,非法值直接抛错(fail fast)。

## 2. 完整示例

```json
{
  "Tarui": {
    "Application": {
      "DeepLinkSchemes": ["tarui"]
    },
    "Notification": {
      "AumId": "dev.Demo",
      "DisplayName": "Tarui Demo"
    },
    "FileAssociations": [
      {
        "Ext": ".tdoc",
        "Name": "Tarui Demo Document",
        "Description": "Demo document owned by the Tarui demo app"
      }
    ],
    "Window": {
      "Title": "Tarui Demo",
      "Width": 1280,
      "Height": 820,
      "Center": true,
      "Decorations": true,
      "Resizable": true,
      "CloseRequestTimeout": 3.0
    },
    "Web": {
      "Policy": { "NavExternal": "https:*" }
    }
  },
  "Logging": { "LogLevel": { "Default": "Information" } }
}
```

## 3. `Tarui:Window:*` 键

| 键 | 作用 | 默认 |
| --- | --- | --- |
| `Title` | 主窗口标题 | `tarui.net` |
| `Url` | 主窗口 URL | 空 → 推断自前端 |
| `Width` / `Height` | 尺寸 | `1280` / `820` |
| `MinWidth` / `MinHeight` | 最小尺寸 | `900` / `600` |
| `MaxWidth` / `MaxHeight` | 最大尺寸 | 不限 |
| `X` / `Y` / `Center` | 启动位置 / 居中 | 居中 |
| `Resizable` / `Decorations` / `AlwaysOnTop` / `Visible` | 窗口外观 | `true` / `true` / `false` / `true` |
| `CloseRequestTimeout` | `core:window|close` 未确认时的强制关闭超时(秒) | `3.0`;`0` 表示必须显式 `force=true` |

`builder.Window.Configure(...)` 代码配置在合并链最末,数值/布尔字段直接覆盖 `appsettings.json`。

## 4. `Tarui:Web:*` 键

详见 [`./webview-resource-mode.md`](webview-resource-mode.md)。

| 键 | 取值 | 说明 |
| --- | --- | --- |
| `Mode` | `http` / `scheme` | 自动推断。`http` 适合 Vite dev 或本地服务器;`scheme` 把 `tarui://localhost` 直接映射到 `frontendDist`,无需 HTTP listener |
| `Url` | `http(s)://...` | 仅 HTTP 模式 |
| `Root` / `Scheme` / `Host` | 路径与 scheme 名 | 仅 Scheme 模式 |
| `SpaFallback` | `true`/`false` | 仅当扩展名缺失的主帧导航 404 时回退到 `index.html` |
| `Csp` | CSP 字符串 | Scheme 模式强制应用 |
| `MaxAssetBytes` | 整数 | 静态资源大小上限,默认 64 MiB |
| `UserAgent` | UA 字符串 | CEF `CefSettings.UserAgent`(仅初始化期) |
| `ProxyServer` | `host:port` | CEF `--proxy-server`(仅初始化期) |
| `CachePath` | 路径 | `CefSettings.CachePath`,缺省走 `TaruiApplicationIdentity` 派生的本地缓存目录 |
| `Policy.NavExternal` | `https:*` / 自定义 glob | 主帧之外允许导航的 origin(走 `WebViewRequestPolicy`) |

环境变量兜底(`TARUI_WEB_MODE` / `TARUI_WEB_URL` / `TARUI_WEB_ROOT` / `TARUI_WEB_CSP` / `TARUI_WEB_USER_AGENT` / `TARUI_WEB_PROXY_SERVER`)等。

## 5. `Tarui:Application:*` 键

| 键 | 作用 |
| --- | --- |
| `DeepLinkSchemes` | 注册的 deep-link schemes(`Tarui:Application:DeepLinkSchemes: ["tarui"]`) |

## 6. `Tarui:Notification:*` 键(Windows)

| 键 | 作用 |
| --- | --- |
| `AumId` | Windows Toast AUMID 注册(默认 = `product.identifier`) |
| `DisplayName` | 通知中心显示名 |

## 7. `Tarui:FileAssociations`(Windows portable zip)

```json
"FileAssociations": [
  {
    "Ext": ".tdoc",
    "Name": "Tarui Demo Document",
    "Description": "Demo document owned by the Tarui demo app"
  }
]
```

运行时由 `WindowsFileAssociationRegistrar` 在 HKCU 注册 ProgID + `.ext` 映射;MSIX 走 `bundle.fileAssociations` + `uap:fileTypeAssociation`;macOS 走 `bundle.macOS.schemes` + InfoPlist `CFBundleDocumentTypes`。

## 8. `Logging:LogLevel:*`

标准 `Microsoft.Extensions.Logging`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Tarui": "Debug"
    }
  }
}
```

## 9. 运行时诊断

```powershell
# 启用 Debug 日志
$env:TARUI_LOG_LEVEL__DEFAULT = "Debug"
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj

# CEF 自身日志
$env:TARUI_WEB_LOG_FILE = "C:\temp\cef.log"

# 平台能力矩阵
# 通过 core:platform|capabilities 命令读取真实可用性
```
