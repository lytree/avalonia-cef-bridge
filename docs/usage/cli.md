# CLI 命令面(tarui)

> `Tarui.Cli`(已发布为 `tarui` 命令,项目入口 `src/tarui-cli/`)的命令形态对齐 `tauri-cli`。
>
> 配套文档:[`./scaffolding.md`](scaffolding.md)、[`./build-and-publish.md`](build-and-publish.md)、[`../design/cli-workflow.md`](../design/cli-workflow.md)。

## 1. 安装与版本

```powershell
dotnet tool install -g Tarui.Cli
tarui --version
```

版本与 `Tarui.*` NuGet 包族 lockstep。

## 2. 命令总览

| 命令 | 作用 |
| --- | --- |
| `tarui init <name>` | 从模板脚手架新应用(`--local <path>` 用于仓库内开发) |
| `tarui dev` | 开发服务器(`build.beforeDevCommand`)+ `dotnet watch`,Ctrl+C 同步拆除 |
| `tarui build` | 前端构建、自包含发布、zip/msix/.app.tar.gz 安装器 + `latest.json` |
| `tarui plugin init <name>` | 脚手架插件骨架(`permissions/`、`guest-js/`、`tests/`;`--local <repo>`) |
| `tarui plugin pack` | 插件预检:布局/权限/版本一致性、自测试、双包打包 |
| `tarui info` | 环境 / 工具链 / 清单诊断 |
| `tarui --help` | 完整命令面 |
| `tarui --version` | CLI 版本 |

CLI 是 **零第三方依赖** 的纯编排器,所有编译/打包仍交给原生工具链(`pnpm`、`dotnet`、`signtool`)。

## 3. `tarui init`

```powershell
tarui init my-app                # 走 NuGet 包,默认发布模式
tarui init my-app --local <repo> # 走 ProjectReference,仓库内开发
```

`--local` 模式下 `LocalReferenceRewriter` 把 `PackageReference` 反向改写为 `ProjectReference`,指向本地 Tarui 源码。

底层等价于 `dotnet new tarui-app`,但 CLI 会:

1. 项目名规范化(C# 标识符 + reverse-DNS identifier)
2. 实例化模板
3. 按结构 JSON 补丁 `product.name` / `identifier`(规避模板占位符被 dotnet new 小写化导致文本替换失效)
4. 可选用 `pnpm install`(manager 不存在时降级警告兜底)

## 4. `tarui dev`

编排时序:

1. 读取并校验 `tarui.app.json`。
2. spawn `build.beforeDevCommand`(如 `pnpm dev`),轮询 `devUrl` 直到 HTTP 可达(超时 60s,失败输出子进程日志尾部)。
3. 以 `TARUI_WEB_MODE=http`、`TARUI_WEB_URL=<devUrl>` 启动 `dotnet watch run --project <desktop>`(可配置退化为 `dotnet run`)。
4. Ctrl+C 时按进程组优雅终止双进程。

热重载边界:

- **前端**:Vite HMR 原生生效(CEF 直连 devUrl 即得,无额外机制)。
- **后端**:`dotnet watch` 全量重启,WebView 状态丢失——与 Tauri Rust 侧重编译语义一致,**不做状态保持**。

dev 专属 profile:约定 `appsettings.Development.json`(ASP.NET Core 标准 `dotnet run` 环境约定)承载 dev 期差异(日志 Debug 级、单实例 channel 加 `-dev` 后缀避免锁冲突、DeepLink scheme 加 dev 后缀),**不引入新机制**。

```powershell
tarui dev --no-watch  # 不带 dotnet watch,普通 dotnet run
```

## 5. `tarui build`

```powershell
tarui build                                   # 默认走 tarui.app.json 的 bundle.targets
tarui build --bundle zip,msix                 # 显式指定
tarui build --rid osx-arm64 --bundle app-bundle
tarui build --out dist-custom                 # 自定义产物目录
```

时序:

1. 执行 `build.beforeBuildCommand`,校验 `frontendDist/index.html` 存在。
2. `dotnet publish -c Release -r <rid> --self-contained`(CEF 内容、web dist、capabilities/schema 由既有 Content 机制进入产物)。
3. 校验 CEF 运行时存在(`runtime/cef/<rid>` 或 runtime 包还原)。
4. 按 `bundle.targets` 打包:
   - **Windows**(`--rid win-x64`):可移植 `zip` + MSIX(`--bundle msix` 或 `bundle.targets: ["zip","msix"]`)
   - **macOS**(`--rid osx-x64` / `osx-arm64`):`.app` / `.app.tar.gz`(`--bundle app-bundle` 或 `bundle.targets: ["app-bundle"]`,需 `bundle.macOS` 块)
   - **Linux**(`--rid linux-x64` / `linux-arm64`):self-contained `zip`(`--bundle zip` 或 `bundle.targets: ["zip"]`;需先 `./eng/cef/install-runtime.ps1 -RuntimeIdentifier linux-x64` 装原生 CEF)
5. 生成产物清单与校验和;`latest.json` 占位(Updater 衔接)。

产物树:

```text
dist/
  <app>-<version>-<rid>.zip
  <app>-<version>-<rid>.msix          # Windows
  <app>.app/                          # macOS(app-bundle)
  <app>-<version>-<rid>.app.tar.gz   # macOS(app-bundle)
  latest.json                         # updater 蓝图:version / url / sha256 / signature
  bin/                                # dotnet publish 原始输出(调试用)
```

无论哪平台,build 都会生成带 SHA-256 的升级器蓝图 `dist/latest.json`。MSIX 由托管实现的 `MsixPacker` 构建(OPC ZIP + `AppxManifest.xml` + SHA-256 `AppxBlockMap.xml`,**不依赖 `makeappx`**);若配置了 `bundle.msix.certificate.{path,password,timeStamperUrl}`,将通过 `signtool.exe` 做 Authenticode 签名,否则产未签名包。

## 6. `tarui plugin`

### 6.1 `tarui plugin init`

```powershell
tarui plugin init tarui-plugin-ocr --local <repo>
```

生成:

```text
tarui-plugin-ocr/
  src/Tarui.Plugins.Ocr/      # Plugin.cs + Contracts.cs + csproj
  permissions/                 # schema.json + default.json + README.md
  guest-js/                    # @lytree/plugin-ocr: package.json + tsconfig + src/
  tests/Tarui.Plugins.Ocr.Tests/  # 自测试 csproj + Program.cs
  examples/demo/README.md      # 接线示例
  README.md                    # 使用 / 权限 / 威胁模型骨架
```

类名 / DI 方法名由插件名规范化推导(`ocr` → `OcrPlugin` / `AddOcrPlugin`),杜绝遗留占位符。`permissions/*.json` 同时设 `Link`(构建输出 `bin/permissions/ocr/`)与 `PackagePath`(nupkg 内 `permissions/ocr/`)双路交付。

### 6.2 `tarui plugin pack`

```powershell
tarui plugin pack
```

五步预检:

1. 布局检测(`src` 恰一个 csproj)
2. 权限一致性(`default.json` 引用必须声明于 `schema.json` 且 id 以 `plugin:` 开头、唯一)
3. 双包版本一致性(csproj `Version` == guest-js `package.json.version`)
4. 运行插件自测试
5. `dotnet pack`(确认 nupkg 含 `permissions/`)+ `npm pack`(guest-js)

发布:`dotnet nuget push` + `npm publish`(lockstep 版本)。

## 7. `tarui info`

```powershell
tarui info
tarui info --config <path>     # 指定 tarui.app.json
```

输出环境 / 工具链(`dotnet` / `pnpm` / `node` / RID)+ 清单诊断结果。
