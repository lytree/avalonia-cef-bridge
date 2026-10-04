# 模板脚手架:`tarui init`

> 模板包 `Tarui.Templates` 提供了 `tarui-app` 模板。CLI(`src/tarui-cli`,`tarui` 命令)把脚手架/构建/打包整合成与 `tauri-cli` 同构的体验。

## 1. 脚手架新应用

```powershell
# 1. 安装 CLI(发布后)
dotnet tool install -g Tarui.Cli

# 2. 新建应用
tarui init my-app
cd my-app

# 3. 开发模式:同时跑 pnpm dev 与 dotnet run
tarui dev

# 4. 生产构建:pnpm build → dotnet publish → zip/msix
tarui build
```

### 1.1 `--local` 模式(仓库内开发)

若你正在修改 Tarui 框架源码并希望新应用直接 `ProjectReference` 而非 NuGet 包:

```powershell
tarui init my-app --local <path-to-tarui-source>
```

`LocalReferenceRewriter` 会把 `PackageReference` 反向改写为 `ProjectReference` 并指向本地 CEF/web 产物根(正则保格式替换),支持仓库内开发。

### 1.2 模板结构

```text
my-app/
  my-app.Desktop/         # csproj (PackageReference: Tarui.Hosting/Shell/WebView.CefGlueNext)
                          # + Program.cs(组合根骨架)
                          # + appsettings.json
                          # + tarui.app.json
  web/                    # 前端工程(React + Vite)
  capabilities/main.json  # 最小权限集
  icons/                  # 应用图标
  README.md               # dev/build 快速上手
```

**默认零插件**(仅 core 基础权限)——安装即最小权限,与"禁止自动授予"一致(Tauri 模板同样以最小 capability 起步)。

## 2. `tarui.app.json` 构建清单

Demo 自带一份可参考的清单(`examples/demo/tarui.app.json`):

```json
{
  "$schema": "https://tarui.dev/schemas/app.v1.json",
  "product": { "name": "demo", "version": "0.4.1", "identifier": "dev.demo" },
  "build": {
    "frontend": "web",
    "beforeDevCommand": "pnpm dev",
    "devUrl": "http://localhost:5173",
    "beforeBuildCommand": "pnpm build",
    "frontendDist": "web/dist",
    "desktopProject": "Demo.Desktop/Demo.Desktop.csproj"
  },
  "bundle": {
    "targets": ["zip"],
    "shortDescription": "A Tarui desktop application",
    "fileAssociations": [
      {
        "ext": ".tdoc",
        "name": "Tarui Demo Document",
        "description": "Demo document owned by the Tarui demo app",
        "mimeType": "application/x-tdoc",
        "role": "Editor"
      }
    ]
  },
  "app": { "capabilities": ["main", "editor"] }
}
```

字段语义:

| 字段 | 作用 |
| --- | --- |
| `product.name` / `product.identifier` | 应用显示名与反写域名标识 |
| `build.frontend` | 前端目录(相对应用根) |
| `build.beforeDevCommand` / `build.devUrl` | dev 模式前置命令、Vite dev server URL(`Tarui:Web:Mode` 自动推断为 `http`) |
| `build.beforeBuildCommand` / `build.frontendDist` | 生产构建前置命令、产物目录(Scheme 模式走 `tarui://localhost`) |
| `build.desktopProject` | 桌面宿主项目相对路径 |
| `bundle.targets` | 安装器目标:`zip`(必选)、`msix`(Windows 商店风格)、`app-bundle`(macOS `.app`/`.app.tar.gz`) |
| `bundle.macOS` | macOS bundle 配置(`bundleId` / `executableName` / `minimumSystemVersion` / `schemes`) |
| `bundle.msix` | Windows MSIX 配置(`publisher` + `certificate.{path,password,timeStamperUrl}`) |
| `bundle.fileAssociations` | 文件关联(MSIX `uap:fileTypeAssociation` + macOS `CFBundleDocumentTypes`) |
| `app.capabilities` | 应用加载的 capability 文件名(不含 `.json`) |

完整 schema 见 [`../../schemas/tarui-app.schema.json`](../../schemas/tarui-app.schema.json)。

## 3. 模板项目结构

模板项目 `src/templates/Tarui.Templates/` 提供 `tarui-app`:

```text
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

## 4. 仓库内开发 Demo

`examples/demo` 直接通过 `ProjectReference` 构建 `src/` 树(不依赖已发布包),因此永远跟踪当前源码。

```powershell
cd examples/demo/Demo.Desktop
dotnet run --project Demo.Desktop.csproj
```

React 前端(`examples/demo/web`)演示窗口 + IPC 状态控制、路由事件以及隔离的 `appData` store/fs 访问。其 `capabilities/main.json` 仅授予 Demo 用到的权限。
