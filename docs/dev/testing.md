# 测试约定

> 控制台式自测试约定、基线门禁、架构测试。

## 1. 测试形态

仓库测试是 **控制台式自测试**,非 xUnit/NUnit。每一个 `tests/Tarui.*.Tests/`:

- `<Name>.Tests.csproj`(`OutputType=Exe`、`net10.0`)。
- `Program.cs` 内 `Main` 串联多个行为用例,使用 `RunCase("BehaviorsName", () => { ... })` 一类 helper,**断言失败必须抛带说明的异常**。
- 测试名用 `PascalCase` 行为描述,如 `DeniesCommandsOutsideCapability`、`RoutesWindowCloseRequestToWebview`。
- 受宿主环境约束的用例放 `.requires-env.txt`(每行一个 ENV 名),未设置时 `eng/test-all.ps1` 标记为 skipped,不计入基线。
- 命名空间使用文件范围,4 空格缩进,异步方法以 `Async` 结尾。

## 2. 自测试模板

```csharp
using Tarui.Ipc;

internal static class Program
{
    public static int Main(string[] args)
    {
        var failures = 0;

        RunCase("RoutesExpectedCommand", () =>
        {
            var router = BuildRouter();
            var ctx = MakeAuthorizedContext();
            var result = router.InvokeAsync(
                ctx,
                "plugin:foo|do",
                """{"value":"hello"}""",
                CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("echo:hello", result.Result.Value);
        });

        RunCase("DeniesCommandsOutsideCapability", () =>
        {
            var router = BuildRouter();
            var ctx = MakeUnauthorizedContext();
            Assert.Throws<NotAuthorizedException>(() =>
                router.InvokeAsync(ctx, "plugin:foo|do", "{}", CancellationToken.None));
        });

        return failures;
    }
}
```

## 3. 基线门禁

`eng/test-all.ps1 -BaselineCount 21`:

```powershell
# 发现 tests/*.Tests/*.Tests.csproj,按字母序 dotnet run;
# 任一失败立即停止;通过数 < 21 时抛错(防止"意外删除测试"回归)
./eng/test-all.ps1 -BaselineCount 21
```

新加测试需要同步调整 Baseline,PR 中评审后修改:

```powershell
# 临时提高基线找缺漏测试
./eng/test-all.ps1 -BaselineCount 22
```

## 4. 架构门禁

`Tarui.Architecture.Tests` 独立运行,做禁反射 / 禁扫描 / 禁 `ActivatorUtilities` / JSON 源生成 / CefGlue 包内容等的静态扫描:

```powershell
dotnet run --project tests/Tarui.Architecture.Tests --no-build
```

可选项:

- 无参:扫描 `src/` 下 active 文件,任何引入 `ActivatorUtilities`、`Assembly.Load*`、`GetAssemblies`、反射 JSON 路径都会被 CI 拒。
- `--require-package --package <path>`:对 `Tarui.WebView.CefGlueNext` 包做组件包内容门禁(包含全部托管 CefGlue DLL 且无 Xilium 包依赖)。

修改依赖或分层后必须跑通:

```powershell
dotnet run --project tests/Tarui.Architecture.Tests -c Release --no-build
```

## 5. 前端测试

`web/packages/api/__tests__` 加 Vitest 单元测试(契约 stub 即可):

```ts
import { describe, it, expect } from 'vitest';
import { invoke } from '../src/ipc';

describe('@lytree/api/window', () => {
  it('calls core:window|set-title', async () => {
    const stub = stubIpc({ result: null });
    await setTitle('Hello');
    expect(stub.lastCall).toEqual({ command: 'core:window|set-title', args: { title: 'Hello' } });
  });
});
```

`pnpm test` + `pnpm build` 0 错误。

## 6. 测试清单(2026-09-19 基线)

仓库已有 26+ 套自测试,涵盖:

- `Tarui.Ipc.Tests` —— CommandRouter / CapabilitySet / Channel
- `Tarui.Plugins.Tests` —— 插件组合 + 路由器
- `Tarui.Shell.Tests` / `Tarui.Hosting.Tests` —— Shell 组合 + Host 生命周期
- `Tarui.Architecture.Tests` —— 反射 / 扫描门禁
- `Tarui.WebView.Tests` —— CefGlue 适配 + Scheme 安全规则
- `Tarui.Http.Tests` —— HTTP 客户端(scope / 重定向 / 流式)
- `Tarui.ShellPlugin.Tests` —— 插件层集成
- `Tarui.DeepLink.Tests` —— DeepLink 桥(Windows + macOS 桥单元)
- `Tarui.Cli.Tests` —— CLI 编排 / 清单校验
- `Tarui.PersistedScope.Tests` / `Tarui.Positioner.Tests` / `Tarui.WebSocket.Tests` —— Phase 4+ 新插件

完整清单见 `tests/` 目录。
