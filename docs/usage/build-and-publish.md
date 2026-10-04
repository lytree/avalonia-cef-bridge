# 桌面发布:MSIX / zip / `.app` / `.app.tar.gz`

> `tarui build` 产物形态、签名、跨平台安装步骤。
>
> 配套文档:[`./cli.md`](cli.md)、[`../adr/0002-macos-real-build-pipeline.md`](../adr/0002-macos-real-build-pipeline.md)。

## 1. Windows:`tarui build --bundle zip,msix`

`tarui build --bundle zip,msix` 会:

1. 调 `dotnet pack` 把生产项目打成 `artifacts/nuget/*.nupkg`。
2. `dotnet publish` 出 `<app>.Desktop.dll` + Avalonia 依赖。
3. 把 `frontendDist` 复制到发布目录,作为 Scheme 模式根。
4. 用 `MsixPacker`(纯托管实现,**不依赖 `makeappx.exe`**)生成:
   - `AppxManifest.xml`(publisher 默认 `CN=Tarui`,`CN=` 内联解析出 `PublisherDisplayName`)
   - 四段版本(`0.1.0` → `0.1.0.0`)
   - RID → `ProcessorArchitecture` 映射
   - full-trust 桌面声明(`runFullTrust` + `windows.fullTrustProcess`)
   - `AppxBlockMap.xml`(SHA-256 分块哈希,全载荷不压缩保证精确)
   - 完整 OPC ZIP 载荷
5. 若配置了 `WINDOWS_CERT_*` 密钥,调用 `signtool.exe` 做 `/fd SHA256` + 可选时间戳;否则产未签名 MSIX。
6. 同时产出 `dist/<app>-<rid>-<version>.zip` 通用安装器。

### 1.1 MSIX 签名配置

```json
{
  "bundle": {
    "targets": ["zip", "msix"],
    "msix": {
      "publisher": "CN=YourCompany",
      "certificate": {
        "path": "path/to/cert.pfx",
        "password": "<env-var>",
        "timeStamperUrl": "http://timestamp.digicert.com"
      }
    }
  }
}
```

GitHub `release` 环境 secrets:

- `WINDOWS_CERT_BASE64` —— base64 编码的 `.pfx`
- `WINDOWS_CERT_PUBLISHER` —— Publisher Subject 名(MSIX `Publisher` 字段)
- `WINDOWS_CERT_PASSWORD` —— `.pfx` 密码
- `WINDOWS_CERT_TIMESTAMP` —— 时间戳 URL(可选)

无证书时 MSIX 以未签名形式产出,可本地调试但不满足 store / 企业分发要求。

## 2. macOS:`tarui build --bundle app-bundle`

`.app` / `.app.tar.gz` 由 `MacOsBundleBuilder` + `InfoPlistBuilder` 拼装(11 个 PLIST 必备键 + `CFBundleURLTypes`,与运行时 `DeepLinkService.Deliver` 校验规则对齐),用 `System.Formats.Tar` 包成 `<name>-<version>-<rid>.app.tar.gz`,SHA-256 一并进 updater blueprint。

```json
{
  "bundle": {
    "targets": ["app-bundle"],
    "macOS": {
      "bundleId": "dev.example.my-app",
      "executableName": "my-app",
      "minimumSystemVersion": "11.0",
      "schemes": ["tarui"]
    }
  }
}
```

`.app.tar.gz` 解包与启动:

```bash
tar -xzf my-app-0.4.1-osx-arm64.app.tar.gz
xattr -dr com.apple.quarantine my-app.app 2>/dev/null || true
chmod +x my-app.app/Contents/MacOS/my-app
open my-app.app
```

> **macOS 产物未签名/未公证**,分发前需 `codesign --deep --sign <identity> my-app.app` 与 `xcrun notarytool submit --wait my-app.zip`(公证票据 `stapler staple my-app.app`),详见 [`../adr/0002-macos-real-build-pipeline.md`](../adr/0002-macos-real-build-pipeline.md) §8。

## 3. Linux:`tarui build --bundle zip`

Linux `linux-x64` self-contained zip 解包与启动(产物体积较大约 400 MB,因为带 .NET 运行时 + 原生 CEF):

```bash
unzip my-app-0.4.1-linux-x64.zip
chmod +x my-app
./my-app
```

> Linux zip 未签名/未打包 deb/rpm/AppImage;首次启动若遇到 `libnss3.so` / `libgtk-3` 等系统库缺失,按发行版包管理器补齐即可(如 Debian/Ubuntu `apt install libnss3 libatk-bridge2.0-0 libgtk-3-0 libasound2 libxshmfence1`)。分发侧可考虑 fpm / electron-builder 等格式(暂未实现,详见 [`../design/cli-workflow.md`](../design/cli-workflow.md) §12 待办)。

## 4. Updater:`dist/latest.json`

`tarui build` 在所有平台都生成 `dist/latest.json`:

```json
{
  "version": "0.4.1",
  "platforms": {
    "windows-x86_64": {
      "url": "https://github.com/.../my-app-0.4.1-win-x64.zip",
      "sha256": "...",
      "signature": "<ECDSA-P384-SHA384>"
    },
    "darwin-arm64": {
      "url": "https://github.com/.../my-app-0.4.1-osx-arm64.app.tar.gz",
      "sha256": "...",
      "signature": "<ECDSA-P384-SHA384>"
    },
    "linux-x86_64": {
      "url": "https://github.com/.../my-app-0.4.1-linux-x64.zip",
      "sha256": "...",
      "signature": "<ECDSA-P384-SHA384>"
    }
  }
}
```

`signature` 字段为 ECDSA(P-384/SHA-384)签名,被 `Tarui.Plugins.Updater` 校验。

## 5. 文件关联

`bundle.fileAssociations`:

```json
{
  "bundle": {
    "fileAssociations": [
      {
        "ext": ".tdoc",
        "name": "Tarui Demo Document",
        "description": "Demo document owned by the Tarui demo app",
        "mimeType": "application/x-tdoc",
        "role": "Editor"
      }
    ]
  }
}
```

落地:

- **MSIX**:`uap:fileTypeAssociation` 声明在 `AppxManifest.xml`。
- **macOS**:`InfoPlistBuilder` 写 `CFBundleDocumentTypes`。
- **portable zip(Windows runtime)**:`WindowsFileAssociationRegistrar` 在 HKCU 注册 ProgID + `.ext` 映射。

## 6. GitHub Actions Release

`.github/workflows/release.yml`(tag `tarui-v<version>` 或手动触发):

1. `pack-and-build` job(Windows `win-x64`,`zip;msix`)
2. `pack-and-build-macos` job(`macos-14`,`app-bundle`)
3. `pack-and-build-linux` job(`ubuntu-latest`,`zip`,self-contained)
4. `pack-and-build-linux-arm64` job
5. 推 NuGet(OIDC trusted publishing,需 `NUGET_USER` secret)
6. 推 npm `@lytree/api`(OIDC provenance,需 `NPM_USER` 关联的 trusted-publisher 配置)
7. `release` job 下载所有产物到 `dist-*`,`Where-Object` 白名单接受 `*.zip` / `*.msix` / `*.app.tar.gz` / `*.gz`,`softprops/action-gh-release` 上传到带产物的 GitHub Release

Release notes 提示 macOS 包未签名/未公证;MSIX 未签名(无证书时)。

## 7. CI 与发布门禁

PR / 分支门禁(`.github/workflows/ci.yml`):

1. `dotnet build` 0 警告
2. `dotnet pack` 产出 `artifacts/nuget/*.nupkg`
3. `Tarui.Architecture.Tests --require-package` 校验
4. 外部 NuGet 消费者冒烟
5. 版本一致性
6. `eng/test-all.ps1 -BaselineCount 21`
7. `Tarui.Architecture.Tests` 反射门禁
8. `pnpm lint` + `pnpm build`

macOS 真机门禁(`.github/workflows/ci-macos.yml`):

- `plutil -lint` + `plutil -extract CFBundleURLTypes`
- `tar -tzf` / `tar -xOf` archive round-trip
- `shasum -a 256 -c` 自校验
- `Tarui.DeepLink.Tests` / `Tarui.Ipc.Tests` / `Tarui.Shell.Tests` 在 Apple Silicon .NET 10 上不退化
