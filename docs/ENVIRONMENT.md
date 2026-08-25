# tarui.net 环境初始化文档

> 面向第一次接触仓库的开发者或 CI 维护者:从零准备 Windows / Linux / macOS 开发与发布环境。
>
> 配套文档:[`USAGE.md`](USAGE.md)(使用文档)、[`DEV.md`](DEV.md)(开发文档)、[`architecture.md`](architecture.md)(架构总览)。

---

## 1. 主机操作系统要求

| OS | 状态 | 备注 |
| --- | --- | --- |
| Windows 10 1809+ / 11 | ✅ 主验证平台 | MSIX 打包仅在此平台进行 |
| Linux (x64 / arm64) | ✅ CEF 原生包就绪 | Avalonia 桌面运行需要 X11 / Wayland;`runtime/cef/<rid>/` |
| macOS 12+ (Intel / Apple Silicon) | ✅ CEF 原生包就绪 | 需要 Xcode 命令行工具;代码签名需本机证书 |

仓库未启用跨平台 CI,但所有产物的 nuspec 与脚本都按三平台设计,本地未验证的路径请按"复现 CI"的思路逐项核实。

---

## 2. 必备工具链

| 工具 | 版本 | 用途 | 安装提示 |
| --- | --- | --- | --- |
| **.NET SDK** | **10.0.400**(允许 latestPatch,不开 prerelease) | 编译、测试、发布、CLI 工具 | `global.json` 锁版本;winget / 官方 install / `dotnet-install.ps1` 都可 |
| **PowerShell** | 7.4+(脚本用 `#requires` 与 `Set-StrictMode`) | 跑 `eng/*.ps1` 与 `release.yml` 工作流 | Windows 自带 5.1,建议并行安装 7 |
| **Node.js** | 22 LTS(>= 20) | 前端 Vite/TS 工具链 | nvm-windows / nvm / volta |
| **pnpm** | **11.15.1**(与 `web/package.json#packageManager` 锁定) | 仓库前端工作区 | `corepack enable && corepack prepare pnpm@11.15.1 --activate` |
| **tar** | 系统自带,需支持 `bzip2` | `eng/cef/install-runtime.ps1` 解压 CEF | macOS/Linux 默认;Windows 10+ 自带 bsdtar |
| **Git** | 2.40+ | 拉取、tag、submodule(本仓库无 submodule) | 任意渠道 |
| **signtool**(可选) | Windows SDK 10.0.22621+ | MSIX Authenticode 签名 | 仅发版需要 |
| **makeappx**(可选) | Windows SDK | MSIX 旧路径 | **本仓库 MSIX 打包不依赖它**(纯托管 `MsixPacker`) |

版本验证一行命令:

```powershell
dotnet --version     # 期望 10.0.4xx
node --version       # 期望 v22.x
pnpm --version       # 期望 11.15.1
pwsh -Command '$PSVersionTable.PSVersion'  # 期望 7.4+
git --version
tar --version | Select-String -Pattern 'bsdtar|gnu tar'
```

---

## 3. Windows 全流程初始化

### 3.1 安装 SDK 与工具链

```powershell
# 选项 A:winget(推荐)
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS.LTS
winget install Microsoft.PowerShell

# 选项 B:dotnet-install.ps1(适合 CI/容器)
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
./dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:ProgramFiles\dotnet"

# corepack 启用 pnpm(免安装)
corepack enable
corepack prepare pnpm@11.15.1 --activate
```

验证 SDK 满足 `global.json`:

```powershell
cd F:\Code\tauri.net
dotnet --version         # 必须 >= 10.0.400
# 若失败,清掉 PATH 上更旧的 SDK 或设置 DOTNET_ROOT
```

### 3.2 还原 NuGet 包

`NuGet.Config` 已锁定源为 `nuget.org`(且清空其他源),可直接使用:

```powershell
dotnet restore tarui.net.sln --configfile NuGet.Config
```

### 3.3 安装 CEF 原生运行时

仅首次需要;`eng/cef/install-runtime.ps1` 下载 CEF 150.0.11 官方包并校验 SHA-1。

```powershell
# 仓库根
./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64
# 可选:win-arm64 / linux-x64 / linux-arm64 / osx-x64 / osx-arm64
# -Force 强制覆盖
# -ValidateOnly 仅校验 SHA-1,不下载/安装
```

脚本行为:

1. 拉 `cef_binary_*_<rid>.tar.bz2` 与对应 `.sha1` sidecar,校验 40 字符十六进制 SHA-1。
2. 解压到临时目录 → 拒绝任何 symlink/hardlink 项 → 拷贝 `Release/` 与 `Resources/` 到 staging。
3. 把现有 `runtime/cef/<rid>/` 改名备份后,原子替换 staging;失败自动回滚。
4. 复制 `src/webview/cefglue/CEF-LICENSE.txt` 到运行时根。
5. 清理临时目录(异常路径写 warning 而非抛错)。

下载失败/校验不通过是网络或镜像源问题;首次安装需稳定访问 `https://cef-builds.spotifycdn.com/`。

### 3.4 构建解决方案

```powershell
dotnet build tarui.net.sln --no-restore
# 期望:0 warnings, 0 errors(TreatWarningsAsErrors=true)
```

### 3.5 安装前端依赖

```powershell
cd web
pnpm install --frozen-lockfile
cd ..
```

`pnpm-workspace.yaml` 声明 `apps/*` 与 `packages/*`,`examples/demo/web` 是独立工作区,其 `pnpm install` 必须在 `examples/demo/web/` 内执行。

### 3.6 跑自测试与 Demo

```powershell
# 21 个自测试项目依次运行
./eng/test-all.ps1 -BaselineCount 21

# 架构门禁
dotnet run --project tests/Tarui.Architecture.Tests --no-build

# 跑仓库内 Demo
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj
```

---

## 4. Linux 初始化(以 Ubuntu 22.04+ 为例)

```bash
# 1. .NET SDK
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
. ~/.bashrc

# 2. Node + pnpm
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt-get install -y nodejs
corepack enable
corepack prepare pnpm@11.15.1 --activate

# 3. 系统库(Avalonia 桌面 + CEF 依赖)
sudo apt-get install -y libx11-dev libxcomposite-dev libxdamage-dev \
    libxrandr-dev libxkbcommon-dev libpango1.0-dev libcairo2-dev \
    libgbm-dev libasound2-dev libnss3 libatk1.0-0 libatk-bridge2.0-0 \
    libdrm2 libxshmfence1 libxext6 fonts-noto-cjk

# 4. CEF 原生运行时
./eng/cef/install-runtime.ps1 -RuntimeIdentifier linux-x64
# 注意:脚本依赖 PowerShell 7,Linux 上装为 'pwsh'

# 5. 还原 + 构建 + 测试
dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln --no-restore
./eng/test-all.ps1 -BaselineCount 21
```

Wayland 需 Avalonia 12.1.1 启用 X11 后端或对接 XWayland。

---

## 5. macOS 初始化

```bash
# 1. .NET SDK
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
export PATH="$HOME/.dotnet:$PATH"

# 2. Xcode 命令行工具
xcode-select --install

# 3. Node + pnpm
brew install node@22
corepack enable
corepack prepare pnpm@11.15.1 --activate

# 4. CEF 原生运行时
./eng/cef/install-runtime.ps1 -RuntimeIdentifier osx-arm64  # 或 osx-x64

# 5. 还原 + 构建 + 测试
dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln --no-restore
./eng/test-all.ps1 -BaselineCount 21
```

代码签名 / 公证(发版时需要):

```bash
# 钥匙串里准备好 Developer ID Application 证书
security find-identity -v -p codesigning
# notarytool 需要 App Store Connect API Key 或 Apple ID
```

---

## 6. 仓库内结构与关键文件

初始化后建议检查以下文件是否正常落地:

```text
F:\Code\tauri.net\
  global.json                   # .NET SDK 10.0.400 latestPatch
  NuGet.Config                  # 仅 nuget.org
  Directory.Build.props         # TaruiVersion=0.1.0 / TreatWarningsAsErrors
  tarui.net.slnx                # 56 个项目,21 个 *.Tests
  runtime/cef/win-x64/          # CEF 原生运行时(由脚本生成)
  artifacts/nuget/              # dotnet pack 产物(可选)
  docs/USAGE.md / DEV.md / ENVIRONMENT.md  # 本次新增的三份中文文档
  .github/workflows/
    ci.yml                      # PR 门禁:build / pack / 架构 / 自测试 / 前端
    release.yml                 # tag 触发:pack / build / publish / release
```

---

## 7. GitHub Actions 复用

CI 与 Release 的关键步骤都设计为本地可复现:

| CI 步骤 | 本地等效命令 |
| --- | --- |
| `actions/setup-dotnet@v4` (10.0.x) | 同 §2 |
| `dotnet restore --configfile NuGet.Config` | 同上 |
| `dotnet build -c Release --no-restore` | `dotnet build tarui.net.sln -c Release --no-restore` |
| `dotnet pack` + 校验 | `dotnet pack tarui.net.sln -c Release --no-build -o artifacts/nuget` |
| `Architecture.Tests --require-package --package` | `dotnet run --project tests/Tarui.Architecture.Tests -c Release --no-build -- --require-package --package artifacts/nuget/CefGlue.Next.Avalonia.0.1.0.nupkg` |
| 外部 NuGet 消费者冒烟 | 复制 `.github/workflows/ci.yml` 中 "External NuGet consumer smoke" 步骤到本地 |
| 版本一致性 | `pnpm exec node -e "console.log(require('./web/packages/api/package.json').version)"` 应等于 `<TaruiVersion>` |
| `./eng/test-all.ps1 -BaselineCount 21` | 同左 |
| `Tarui.Architecture.Tests`(无参) | `dotnet run --project tests/Tarui.Architecture.Tests -c Release --no-build` |
| `pnpm install --frozen-lockfile` + `lint` + `build` | `cd web; pnpm install --frozen-lockfile; pnpm lint; pnpm build` |

发布 secrets(在 GitHub `release` 环境):

- `NUGET_USER`:nuget.org 用户名(profile name,而非 email)。
- `NPM_USER`:与 npm trusted-publisher 关联的 GitHub 用户名。
- `WINDOWS_CERT_BASE64` / `WINDOWS_CERT_PUBLISHER` / `WINDOWS_CERT_PASSWORD` / `WINDOWS_CERT_TIMESTAMP`(可选):MSIX Authenticode 签名。

nuget.org 与 npmjs.com 上需预先配置 trusted publishing(OIDC),允许 `release` 环境 + `release.yml` 工作流文件名,**无需长寿命 API key**。

---

## 8. 常见环境问题

| 现象 | 处置 |
| --- | --- |
| `dotnet --version` 输出 8.x / 9.x | `global.json` 锁版本,PATH 上有更旧 SDK 时设 `DOTNET_ROOT` 或调整 PATH |
| `dotnet build` 大量 CS1591 警告 | 仓库已全局抑制;若复现说明有项目覆写了 `NoWarn`,检查 `.csproj` |
| `pnpm install` 报 `ERR_PNPM_BAD_PM_VERSION` | `corepack prepare pnpm@11.15.1 --activate`,确保 `pnpm --version` 等于 11.15.1 |
| CEF 安装脚本报 `system tar with bzip2 support is required` | Windows 10 1809+ 自带 `bsdtar`;若使用别名 `tar.exe`(如 Git for Windows),把它从 PATH 移除或调用绝对路径 |
| CEF SHA-1 校验失败 | 检查网络代理;或手动从 https://cef-builds.spotifycdn.com/ 下载并替换 |
| `dotnet run --project Demo.Desktop` 报 `Microsoft.WindowsDesktop.App` 缺失 | SDK 装的是 runtime 而非 SDK,或 Windows 版本 < 10.0.17763 |
| `dotnet pack` 警告 NU5128(缺 README) | 不再警告:`Directory.Build.props` 已强制把仓库根 `README.md` 放进所有可打包包 |
| `tests/Tarui.Architecture.Tests` 失败提示"反射相关 API" | 检查是否新增了 `ActivatorUtilities.CreateInstance`、`Assembly.Load*`、`MethodInfo.Invoke`;新增的 DI 改用 `AddSingleton<T>` |
| 第一次跑 `MsixPacker` 时 `signtool.exe not found` | 配置 `WINDOWS_CERT_*` 或忽略(未签名 MSIX 仍可生成) |
| Avalonia 启动黑屏 | 确认 `runtime/cef/<rid>/` 已安装,Scheme 模式下确认 `frontendDist` 已构建 |
| 测试被标 `[skip]` | 项目根 `.requires-env.txt` 列出的环境变量未设置;补齐后重跑 |
| `eng/test-all.ps1` 报 `Passed count below baseline` | 检查是否有项目被无意中删除;新加测试需要同步调整 Baseline |

---

## 9. 清理与重置

```powershell
# 清理构建产物
dotnet clean tarui.net.sln
Remove-Item -Recurse -Force artifacts, dist, .out, examples/demo/web/dist, examples/demo/web/node_modules

# 清理 CEF 运行时(谨慎:下次需要联网下载)
Remove-Item -Recurse -Force runtime/cef/<rid>

# 清理前端缓存
Remove-Item -Recurse -Force web/node_modules, web/packages/api/node_modules, web/apps/Tarui.Web/node_modules
Remove-Item -Force web/pnpm-lock.yaml.bak

# 清空 NuGet 本地缓存(慎用,会影响其他项目)
dotnet nuget locals all --clear
```

重新初始化:回到 §3 从 `dotnet restore` 开始。

---

## 10. 推荐 IDE 配置

- **Visual Studio 2022 17.12+**(或 VS 2026 预览):装 ".NET 10"、"Avalonia for Visual Studio"、".NET Async Tooling" 工作负载。
- **JetBrains Rider 2025.2+**:开启 Avalonia 插件,设置 `global.json` 为项目 SDK。
- **VS Code**:装 C# Dev Kit、Avalonia for VS Code、ESLint、Oxlint、Vitest Explorer 扩展。

`.vscode/launch.json` / `tasks.json` 已存在模板,首次打开会询问是否信任;`.editorconfig` 统一 4 空格缩进、C# 文件范围命名空间。

---

## 11. 验证清单(完成后逐项确认)

- [ ] `dotnet --version` 输出 10.0.4xx
- [ ] `node --version` 输出 v22.x
- [ ] `pnpm --version` 输出 11.15.1
- [ ] `runtime/cef/<rid>/Release/cef.dll`(或对应平台 lib)存在
- [ ] `dotnet restore tarui.net.sln --configfile NuGet.Config` 0 错误
- [ ] `dotnet build tarui.net.sln -c Release --no-restore` 0 warnings / 0 errors
- [ ] `cd web; pnpm install --frozen-lockfile` 成功
- [ ] `cd web; pnpm build` 成功
- [ ] `./eng/test-all.ps1 -BaselineCount 21` 全部通过
- [ ] `dotnet run --project tests/Tarui.Architecture.Tests -c Release --no-build` 通过
- [ ] `dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj` 弹出主窗口

任何一项不通过都不要继续往下做;返回错误信息对照 §8 排查。
