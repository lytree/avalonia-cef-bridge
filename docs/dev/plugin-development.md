# 插件开发

> 新增 / 修改插件的步骤、解剖、自测试、与前端桥接。

## 1. 插件目录解剖

每个 `Tarui.Plugins.*` 项目保持同一形态:

```text
Tarui.Plugins.Foo/
  Tarui.Plugins.Foo.csproj   # ProjectReference Tarui.Ipc + Tarui.Contracts
  FooPlugin.cs               # : ITaruiPlugin, ConfigureCommands(builder)
  FooCommands.cs             # 静态 handler 方法
  FooScope.cs                # 可选:PathScope/快捷键 scope 解析
  FooJsonContext.cs          # 子 JsonSerializerContext(注册插件专属 DTO)
  InternalsVisibleTo("Tarui.Plugins.Tests")  # 仅集成测试可见 internal
```

对应 NuGet 包 `Tarui.Plugins.Foo` 的元数据由 `Directory.Build.props` 统一写入;不要在 `csproj` 里覆写 `Version`、`Authors`、`PackageId`、`GenerateDocumentationFile`、`IncludeSymbols`。

## 2. Plugin 类与命令注册

```csharp
public sealed class WindowPlugin(IWindowService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new WindowCommands(service);
        commands.Add(
            "core:window|create",
            TaruiJsonContext.Default.WindowOptions,
            TaruiJsonContext.Default.WindowOptions,
            handlers.CreateAsync,
            "core:window|create");
    }
}
```

`commands.Add(...)` 的参数顺序:

```csharp
public CommandRouterBuilder Add(
    string commandId,
    JsonTypeInfo<TArgs> argsTypeInfo,
    JsonTypeInfo<TResult> resultTypeInfo,
    Func<CommandContext, TArgs, CancellationToken, ValueTask<TResult>> handler,
    string permissionId);
```

permission ID 与 command ID 通常一致(`core:window|create` 等);`RegisteredPermissions` 在 `Add` 时自动登记,**禁止手工维护**。

## 3. 现有插件清单

| 插件包 | 命令前缀 | 命令数 | 备注 |
| --- | --- | --- | --- |
| `Tarui.Plugins.Core` | `core:app|*` | 1 | 壳握手 `get-info` |
| `Tarui.Plugins.Window` | `core:window|*` | 24 | 窗口生命周期、几何、装饰、监视器 |
| `Tarui.Plugins.Webview` | `plugin:webview|*` | 5+ | 当前 Webview + 多 Webview 寻址 + DevTools / Eval |
| `Tarui.Plugins.WindowState` | `plugin:window-state|*` | 3 | 持久化窗口尺寸/位置 |
| `Tarui.Plugins.Event` | `core:event|*` | 1 | `emit` |
| `Tarui.Plugins.Dialog` | `plugin:dialog|*` | 2+ | 文件/目录/消息/确认/ask |
| `Tarui.Plugins.System` | `core:path\|core:os\|core:process\|core:shell\|core:clipboard\|core:cli\|core:channel` | 14+ | 平台能力 |
| `Tarui.Plugins.FileSystem` | `plugin:fs|*` | 16 | 受 `PathScope` 限制 + 流式 |
| `Tarui.Plugins.Menu` | `plugin:menu|*` | 7 | 原生菜单 + 上下文菜单 |
| `Tarui.Plugins.Tray` | `plugin:tray|*` | 6 | 系统托盘 |
| `Tarui.Plugins.Notification` | `plugin:notification|*` | 4 | 系统通知 |
| `Tarui.Plugins.Autostart` | `plugin:autostart|*` | 3 | 开机自启(三平台) |
| `Tarui.Plugins.GlobalShortcut` | `plugin:global-shortcut|*` | 4 | 系统级快捷键 |
| `Tarui.Plugins.Store` | `plugin:store|*` | 6 | KV 持久化 |
| `Tarui.Plugins.Log` | `plugin:log|*` | 1 | 日志落盘 |
| `Tarui.Plugins.DeepLink` | `plugin:deep-link|*` | 2 | 自定义 scheme 路由 |
| `Tarui.Plugins.Updater` | `plugin:updater|*` | 3 | 检查 / 下载 / apply |
| `Tarui.Plugins.Http` | `plugin:http|*` | 2 | fetch + upload |
| `Tarui.Plugins.Shell` | `plugin:shell|*` | 4 | spawn / stdin / kill |
| `Tarui.Plugins.Cookie` | `plugin:cookie|*` | 4 | list / set / remove / flush |
| `Tarui.Plugins.Websocket` | `plugin:websocket|*` | 3 | connect / send / close |
| `Tarui.Plugins.PersistedScope` | `plugin:persisted-scope|*` | 3 | 运行时 scope 变更 |
| `Tarui.Plugins.Positioner` | `plugin:positioner|*` | 1 | set-position |

## 4. 新增插件步骤

1. **DTO record + JSON 元数据**:在 `src/core/Tarui.Contracts/` 新增 record 并在 `TaruiJsonContext` 注册。
2. **插件项目**:在 `src/plugins/` 新建 `Tarui.Plugins.Foo/` 项目,`FooPlugin.cs` 实现 `ITaruiPlugin.ConfigureCommands(CommandRouterBuilder)`。
3. **DI 扩展**:同项目 `FooPluginServiceCollectionExtensions.AddFooPlugin()`。
4. **能力清单**:在 `examples/demo/capabilities/*.json` 加入 `plugin:foo|do`(必要时细化 scope)。
5. **前端桥接**:`web/packages/api/src/foo.ts` + `package.json` 的 `exports` 添加 `"./foo"`;`web/packages/api/src/index.ts` barrel 导出。
6. **自测试**:`tests/Tarui.Plugins.Foo.Tests/FooPlugin.Tests.csproj`,控制台式自测试。
7. **架构门禁**:`dotnet run --project tests/Tarui.Architecture.Tests` 通过。
8. **文档同步**:在 [`../design/alignment-plan.md`](../design/alignment-plan.md) §15 状态表登记,在 [`../usage/plugins.md`](../usage/plugins.md) 描述用法。

## 5. 自测试模板

控制台式自测试项目(`OutputType=Exe`、`net10.0`):

```csharp
// tests/Tarui.Plugins.Foo.Tests/Program.cs
using Tarui.Plugins.Foo;
using Tarui.Ipc;

RunCase("RoutesExpectedCommand", () =>
{
    var router = new CommandRouterBuilder()
        .Add(
            "plugin:foo|do",
            FooJsonContext.Default.DoArgs,
            FooJsonContext.Default.DoResult,
            (ctx, args, ct) => ValueTask.FromResult(new DoResult("ok")),
            "plugin:foo|do")
        .Build();

    var ctx = new CommandContext(/* capability allows plugin:foo|do */);
    var result = router.InvokeAsync(ctx, "plugin:foo|do", """{"value":42}""", CancellationToken.None);
    Assert.Equal("ok", result.Result.Value);
});

RunCase("DeniesCommandsOutsideCapability", () =>
{
    var ctx = new CommandContext(/* capability does NOT allow */);
    Assert.Throws<NotAuthorizedException>(() => /* invoke */);
});
```

测试名用 `PascalCase` 行为描述,如 `RoutesExpectedCommand`、`DeniesCommandsOutsideCapability`。断言失败必须抛带说明的异常。受宿主环境约束的用例放 `.requires-env.txt`(每行一个 ENV 名),未设置时 `eng/test-all.ps1` 标记为 skipped,不计入基线。

## 6. 修改现有插件

修改现有插件命令时遵循 IPC 协议三处同步规则(详见 [`ipc-protocol.md`](ipc-protocol.md)):

| 位置 | 文件 | 改动 |
| --- | --- | --- |
| 后端 DTO | `src/core/Tarui.Contracts/**` | 增改 record,实现 `ITaruiCommand<TArg,TRes>` 或事件 record |
| JSON 元数据 | 同上,加 `[JsonSerializable(typeof(...))]` 进 `TaruiJsonContext` | 必须 |
| 后端处理器 | `src/plugins/Tarui.Plugins.Foo/FooPlugin.cs` | `ConfigureCommands` 中 `commands.Add(...)` |
| 前端桥接 | `web/packages/api/src/foo.ts` | 类型 + 调用包装 |
| 包导出 | `web/packages/api/package.json` | 新增/调整 `"./foo"` 子路径 |
| 能力清单 | `examples/demo/capabilities/*.json` 或用户 app 的 `capabilities/*.json` | 加入/调整权限 |

## 7. 第三方 out-of-tree 插件

应用开发者视角的接入流程见 [`../usage/plugins.md`](../usage/plugins.md)。框架维护者需保证:

- 第三方插件 **不触碰** `Tarui.Contracts` 核心契约 — 第三方应有自己的 `JsonSerializerContext`(`FooJsonContext`),与 `TaruiJsonContext` 平行。
- 架构门禁对 in-tree 严格(`src/plugins/` 内),对 out-of-tree 仅以文档与 `tarui plugin pack` 预检约束。
- `tarui plugin init <name>`(详见 [`../design/cli-workflow.md`](../design/cli-workflow.md) §W4)生成完整插件骨架,集成自测试。
