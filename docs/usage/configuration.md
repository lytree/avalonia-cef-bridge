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
      "Resizable": true
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
| `NoRedirectionBitmap` | Windows 专用:去掉原生窗口重定向表面(对标 Tauri `noRedirectionBitmap`),供 DirectComposition 透明窗口使用;HWND 创建后 best-effort 应用,非 Windows 忽略 | `false` |

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
| `Asset.Roots` | 逗号分隔的绝对目录 | 启用 `asset://localhost/<编码路径>` 受限文件通道(配合 `convertFileSrc`);留空即禁用 |
| `Asset.MaxAssetBytes` | 整数 | 单个 `asset://` 文件大小上限,默认 64 MiB;非法值启动即失败 |
| `UserAgent` | UA 字符串 | CEF `CefSettings.UserAgent`(仅初始化期) |
| `ProxyServer` | `host:port` | CEF `--proxy-server`(仅初始化期) |
| `CachePath` | 路径 | `CefSettings.CachePath`,缺省走 `TaruiApplicationIdentity` 派生的本地缓存目录 |
| `Policy.NavExternal` | `https:*` / 自定义 glob | 主帧之外允许导航的 origin(走 `WebViewRequestPolicy`) |
| `Policy.LimitToAppBoundDomains` | `true`/`false` | 对标 Tauri `limitNavigationsToAppBoundDomains`:开启后先做 app-bound 闸门,未命中 `AppBoundDomains` 的导航一律拒绝(连 `NavExternal` 也不放行) |
| `Policy.AppBoundDomains` | 逗号分隔的 host glob | app-bound 域列表(`app.example` / `*.app.example`);缺省为应用 origin host + `localhost` / `127.0.0.1`。开启限制但列表为空时拒绝所有导航 |

环境变量兜底(`TARUI_WEB_MODE` / `TARUI_WEB_URL` / `TARUI_WEB_ROOT` / `TARUI_WEB_CSP` / `TARUI_WEB_USER_AGENT` / `TARUI_WEB_PROXY_SERVER`)等。

## 5. `Tarui:Resources:*` 键(原生资源目录)

应用自带资源(图标、种子数据、模板、数据库初始文件)统一走 `resources` 目录,与 web 资源(§4)是两套边界。文件系统插件的 `resources` 基目录、`ITaruiResourceDirectory` 与打包布局共享同一根,避免出现第二个资源边界。

| 键 | 取值 | 说明 |
| --- | --- | --- |
| `Root` | 路径 | 打包资源根。缺省依次回退 `TARUI_RESOURCE_DIR` → `{可执行文件目录}/resources`(存在时) → 可执行文件目录 |
| `DevelopmentRoot` | 路径 | 开发期源码资源根。存在时**优先于** `Root`,资源改动即时可见,不经过任何拷贝步骤 |
| `MirrorDirectory` | 路径 | remap 资源镜像目录(通常是可执行文件目录);缺省不镜像 |
| `MirroredResources` | 相对路径数组 | 需要镜像的 remap 资源,如 `[ "assets/seed.db" ]` |

环境变量兜底:`TARUI_RESOURCE_DIR` / `TARUI_RESOURCE_DEV_DIR` / `TARUI_RESOURCE_MIRROR_DIR` / `TARUI_RESOURCE_MIRRORED`(逗号分隔)。

```jsonc
{
  "Tarui": {
    "Resources": {
      "Root": "resources",
      "DevelopmentRoot": "../../resources",
      "MirrorDirectory": ".",
      "MirroredResources": ["assets/seed.db"]
    }
  }
}
```

镜像在首次访问时执行一次:目标文件已是最新(长度与时间戳一致)则跳过,因此重启不会重写全部资源。资源查找只接受相对路径,越界、绝对路径与符号链接逃逸由 `IFileAccessPolicy` 统一拒绝。

## 6. `Tarui:Application:*` 键

| 键 | 作用 |
| --- | --- |
| `DeepLinkSchemes` | 注册的 deep-link schemes(`Tarui:Application:DeepLinkSchemes: ["tarui"]`) |
| `Directories.AppData` / `AppLocalData` / `AppConfig` / `AppCache` / `AppLog` | 覆盖对应的 `app_*_dir` 基目录(便携应用)。值必须是绝对路径;空值、相对路径或控制字符在启动时快速失败。覆盖同时作用于 fs/store/sql/persisted-scope 与 `core:path\|resolve`;`temp` / `resources` 不受影响 |

```jsonc
{
  "Tarui": {
    "Application": {
      "Directories": {
        "AppData": "D:\\Portable\\data",
        "AppLocalData": "D:\\Portable\\local",
        "AppConfig": "D:\\Portable\\config",
        "AppCache": "D:\\Portable\\cache",
        "AppLog": "D:\\Portable\\logs"
      }
    }
  }
}
```

### `Tarui:Application:Updater:*` 键(更新插件)

`AddUpdaterPlugin()` 从该 section 读取设置;缺失 `ManifestUrl` / `PublicKey` 时视为未配置,`check` / `download` 返回 `updater-not-configured`,不会启动失败。

| 键 | 作用 | 默认 |
| --- | --- | --- |
| `ManifestUrl` | 签名清单地址(仅 `http` / `https`) | 必填 |
| `PublicKey` | base64 DER SubjectPublicKeyInfo(ECDSA P-384 / SHA-384) | 必填 |
| `CurrentVersion` | 版本比较基线 | `0.1.0` |
| `StagingDir` | 已验证文件的隔离暂存目录(不写运行中的安装目录) | `%LocalAppData%/tarui.net/updater/staging` |
| `MaxManifestBytes` / `MaxFileBytes` / `MaxTotalBytes` | 清单与文件/总量的资源上限 | 1 MiB / 200 MiB / 1 GiB |
| `AllowInsecureHttp` | 允许 `http` 清单地址 | `false` |
| `AllowDowngrades` | 对齐 Tauri 2.12 应用级 `allowDowngrades`:开启后版本**不同**即算更新(默认只接受更高版本) | `false` |

```jsonc
{
  "Tarui": {
    "Application": {
      "Updater": {
        "ManifestUrl": "https://updates.example.com/latest.json",
        "PublicKey": "<base64 DER SubjectPublicKeyInfo>",
        "AllowDowngrades": false
      }
    }
  }
}
```

## 7. `Tarui:Notification:*` 键(Windows)

| 键 | 作用 |
| --- | --- |
| `AumId` | Windows Toast AUMID 注册(默认 = `product.identifier`) |
| `DisplayName` | 通知中心显示名 |

## 8. `Tarui:FileAssociations`(Windows portable zip)

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

## 9. `Logging:LogLevel:*`

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

## 10. 运行时诊断

```powershell
# 启用 Debug 日志
$env:TARUI_LOG_LEVEL__DEFAULT = "Debug"
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj --framework net10.0

# CEF 自身日志
$env:TARUI_WEB_LOG_FILE = "C:\temp\cef.log"

# 平台能力矩阵
# 通过 core:platform|capabilities 命令读取真实可用性
```
