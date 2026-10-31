# CI / CD

> Release 工作流、OIDC 发布、签名密钥、版本策略。

## 1. Release 工作流(`.github/workflows/release.yml`)

tag `tarui-v<version>`(或 manual trigger):

1. 校验 tag 格式 + 版本单一性(`Directory.Build.props` / `@lytree/api` / `tarui.app.json`)。
2. 三平台并行，每个平台独立执行 `dotnet build`(0 警告)→ `./eng/test-all.ps1` 自测试 → `dotnet pack` → `tarui build` 产出平台 Demo:
   - `pack-and-build` job(Windows `win-x64`,`bundle.targets: zip;msix`)。
   - `pack-and-build-macos` job(`macos-14`,`bundle.targets: app-bundle`)。
   - `pack-and-build-linux` job(`ubuntu-latest`,`bundle.targets: zip`,self-contained)。
   三者全部成功后才进入后续阶段。
3. `package-gate` job(Ubuntu,下载 Windows 的 nupkg)跑包门禁:架构 `--require-package` + 外部 NuGet 消费者冒烟。
4. 推 NuGet(OIDC trusted publishing,需 `NUGET_USER` secret)。
5. 推 npm `@lytree/api`(OIDC provenance,需 `NPM_USER` 关联的 trusted-publisher 配置)。
6. `release` job 下载 Windows / macOS / Linux 产物到 `dist-*`,`Where-Object` 白名单接受 `*.zip` / `*.msix` / `*.app.tar.gz` / `*.gz`,`softprops/action-gh-release` 上传到带产物的 GitHub Release。

Release notes 模板提示 macOS 包未签名/未公证;MSIX 未签名(无证书时)。

## 2. 签名密钥(GitHub `release` 环境)

- `NUGET_USER`:nuget.org 用户名(profile name,而非 email)。
- `NPM_USER`:与 npm trusted-publisher 关联的 GitHub 用户名。
- `WINDOWS_CERT_BASE64` / `WINDOWS_CERT_PUBLISHER` / `WINDOWS_CERT_PASSWORD` / `WINDOWS_CERT_TIMESTAMP`(可选):MSIX Authenticode 签名。
- `MACOS_CODESIGN_IDENTITY` / `MACOS_NOTARY_KEY_ID` / `MACOS_NOTARY_ISSUER_ID`(可选,待解锁):macOS 真机发版前见 [`../adr/0002-macos-real-build-pipeline.md`](../adr/0002-macos-real-build-pipeline.md) §8。

## 3. OIDC trusted publishing

nuget.org 与 npmjs.com 上需预先配置 trusted publishing(OIDC),允许 `release` 环境 + `release.yml` 工作流文件名,**无需长寿命 API key**。

- NuGet 配置步骤:[trusted publishing 文档](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),添加 `NUGET_USER` 环境密钥(nuget.org profile 名,不是邮箱)。
- npm 配置步骤:[provenance 文档](https://docs.npmjs.com/generating-provenance-statements),为该仓库添加 `NPM_USER` 关联的 trusted-publisher 条目。

## 4. 版本策略

- `Directory.Build.props#TaruiVersion` 是单源。
- 所有可打包项目 `Version` 继承 `TaruiVersion`。
- `web/packages/api/package.json#version` 与 `TaruiVersion` 同步(发布门禁守护)。
- 0.0.x 视为 preview 通道;1.0 后保持 lockstep + 兼容窗口。
- prerelease 通道:版本后缀 `-preview.N`,同流水线,推送至独立 feed 权限组(尚未启用,详见 [`../design/cli-workflow.md`](../design/cli-workflow.md) §6.3)。

## 5. 失败排查

| 现象 | 排查 |
| --- | --- |
| `dotnet build` 0 warnings 失败 | 检查新增注释规范或 `#pragma warning disable` 是否漏写 PR 说明 |
| 架构门禁失败 | 静态扫描引用了禁用的 API;对照 `tests/Tarui.Architecture.Tests` 的正则 |
| 外部 NuGet 消费者 smoke 失败 | 检查 nuspec 字段是否齐全、版本号是否一致 |
| 版本一致性失败 | `TaruiVersion` 与 `@lytree/api/package.json#version` 不等 |
| `eng/test-all.ps1` Baseline 不达标 | 新加测试但未调整 Baseline,或无意中删除测试 |
| `pnpm install --frozen-lockfile` 失败 | pnpm 版本不一致(`corepack prepare pnpm@11.15.1 --activate`) |
| Release `release` job 找不到产物 | `Where-Object` 白名单或 artifact retention 不匹配 |
