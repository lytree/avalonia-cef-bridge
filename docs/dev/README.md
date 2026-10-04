# 开发文档

> 面向 **框架贡献者**:理解代码库分层、扩展插件、修改 Shell/Hosting/Ipc、维护架构门禁。
>
> 配套文档:[`../usage/`](../usage/README.md)(应用开发者)、[`../design/`](../design/README.md)(架构与评审)。

## 目录

| 文档 | 内容 |
| --- | --- |
| [`environment.md`](environment.md) | Windows / Linux / macOS 开发与发布环境初始化 |
| [`codebase-map.md`](codebase-map.md) | 代码库地图、模块职责、依赖方向、关键不变量 |
| [`plugin-development.md`](plugin-development.md) | 新增 / 修改插件的步骤、解剖、自测试 |
| [`ipc-protocol.md`](ipc-protocol.md) | 修改 IPC 协议的三处同步规则、前端桥接 |
| [`webview-development.md`](webview-development.md) | WebView / CefGlue 适配层开发、CefGlue 内置源码边界 |
| [`frontend-sdk.md`](frontend-sdk.md) | `@lytree/api` 桥接包开发、模块命名约定 |
| [`testing.md`](testing.md) | 控制台式自测试约定、基线门禁、架构测试 |
| [`ci-cd.md`](ci-cd.md) | CI / Release 工作流、OIDC 发布、签名密钥 |
| [`local-workflow.md`](local-workflow.md) | 本地提交流程、Conventional Commit、Agent 协作 |
| [`debugging.md`](debugging.md) | 调试与诊断技巧、问题排查速查 |

## 5 分钟跑起来

```powershell
# 仓库根
git clone <repo> && cd tarui.net

# 一次性:CEF 原生运行时(首次需要联网)
./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64

# 还原 + 构建
dotnet restore tarui.net.slnx --configfile NuGet.Config
dotnet build tarui.net.slnx --no-restore

# 跑自测试与架构门禁
./eng/test-all.ps1 -BaselineCount 21
dotnet run --project tests/Tarui.Architecture.Tests --no-build

# 跑仓库内 Demo
dotnet run --project examples/demo/Demo.Desktop/Demo.Desktop.csproj
```

详细步骤见 [`environment.md`](environment.md);前端工具链初始化见 [`frontend-sdk.md`](frontend-sdk.md)。
