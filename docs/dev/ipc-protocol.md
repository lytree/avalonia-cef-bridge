# 修改 IPC 协议

> 修改 IPC 协议的三处同步规则、前端桥接、向后兼容。

## 1. 同步清单

IPC 是契约,**改动需三处同步**且保持向后兼容:

| 位置 | 文件 | 改动 |
| --- | --- | --- |
| 后端 DTO | `src/core/Tarui.Contracts/**` | 新增 record,实现 `ITaruiCommand<TArg,TRes>` 或事件 record |
| JSON 元数据 | 同上,加 `[JsonSerializable(typeof(...))]` 进 `TaruiJsonContext` | 必须 |
| 后端处理器 | `src/plugins/Tarui.Plugins.Foo/FooPlugin.cs` | `ConfigureCommands` 中 `commands.Add("plugin:foo|do", DoHandler, "plugin:foo|do")` |
| 前端桥接 | `web/packages/api/src/foo.ts` | 类型 + 调用包装 |
| 包导出 | `web/packages/api/package.json` | 新增 `"./foo"` 子路径 |
| 能力清单 | `examples/demo/capabilities/*.json` 或用户 app 的 `capabilities/*.json` | 加入 `plugin:foo|do` |

## 2. 命令 ID 与权限 ID 命名

- **命令 ID**:`<prefix>:<verb>|<action>`,前缀 `core`/`plugin`,动作小写、横线分隔,如 `plugin:fs|read-file-stream`。
- **权限 ID** 与命令 ID 一一对应;`RegisteredPermissions` 自动登记。
- **事件 ID**:`<prefix>://<verb>`,前缀由插件或 shell 保留(`app://`、`window://`、`shell://`、`webview://`、`tray://`、`notification://`、`menu://`、`global-shortcut://`、`updater://`、`fs://`、`log://`、`scope://`、`deeplink://`、`core://`、`user://`),Web 不可发起保留前缀,只允许 `user://*`。

## 3. Capability 同步

新增命令必须:

1. 在 `FooPlugin.ConfigureCommands` 中 `commands.Add(...)` 注册。
2. `CommandRouter.RegisteredPermissions` 会自动收录 — 不要手工登记。
3. `CommandRouterComposer.Compose(...)` 在启动期校验:若 capability 文件中出现未注册权限(跳过 `"*"` 通配)即抛 `InvalidOperationException`。这是 fail-fast 门禁。
4. 把新权限加进 `examples/demo/capabilities/*.json`(可选,但推荐 — Demo 是文档化的活样本)。

## 4. DTO 与 JSON 元数据

```csharp
// src/core/Tarui.Contracts/FooContracts.cs
public sealed record DoFooArgs(string Value);
public sealed record DoFooResult(string Echo);

[JsonSerializable(typeof(DoFooArgs))]
[JsonSerializable(typeof(DoFooResult))]
public partial class TaruiJsonContext : JsonSerializerContext { /* ... */ }
```

`TaruiJsonContext` 是 `partial`,所有跨进程 DTO 都通过 `[JsonSerializable(typeof(...))]` 标注。`Tarui.Ipc.Generators` 进一步为插件生成强类型 invoker stub(命令目录)。

## 5. 前端桥接

```ts
// web/packages/api/src/foo.ts
import { invoke } from './ipc';

export async function doFoo(value: string): Promise<string> {
  const res = await invoke<{ echo: string }>('plugin:foo|do', { value });
  return res.echo;
}
```

并在 `web/packages/api/src/index.ts` barrel 导出(必要时重命名,如 `openDialog` / `openExternal`),在 `web/packages/api/package.json` 的 `exports` 添加 `"./foo"`。

```jsonc
// web/packages/api/package.json
{
  "exports": {
    ".": "./dist/index.js",
    "./foo": "./dist/foo.js",
    // ...
  }
}
```

## 6. 禁止事项

- **禁止** 在 `IpcDispatcher` 之外添加全局 IPC 入口(避免绕开 capability 校验)。
- **禁止** 用反射序列化 DTO(无 `JsonSerializerContext` 标注)。
- **禁止** 在前端用裸 `invoke('plugin:foo|do', ...)` 取代 `@lytree/api/foo`(失去类型 + 失去 capability 错误码透传)。
- **禁止** 让 Web 侧发起保留前缀事件(`app://`、`window://`、`shell://` 等) — 架构门禁会拒。
- **禁止** 修改现有命令 ID、权限 ID、DTO 字段语义(破坏线协议)。需要改名/改语义时新增命令,旧命令标记 deprecated 并保留实现。

## 7. 自测试

IPC 改动必须新增/调整以下测试:

- `tests/Tarui.Ipc.Tests` —— `CommandRouter` 路由 / Capability 拒绝 / 错误码透传
- `tests/Tarui.Plugins.Foo.Tests` —— Foo 插件自身行为
- `tests/Tarui.Architecture.Tests` —— 反射 / 扫描 / `ActivatorUtilities` 静态扫描
- `web/packages/api/__tests__` —— Vitest 契约 stub 测试
