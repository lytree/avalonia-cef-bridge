# 插件系统

> 插件契约、`ITaruiPlugin` / `Add*Plugin()`、权限命名、Scope allow/deny、模板与发布。

## 1. 契约

每个 `Tarui.Plugins.*` 项目保持同一形态:

```text
Tarui.Plugins.Foo/
  Tarui.Plugins.Foo.csproj   # ProjectReference Tarui.Ipc + Tarui.Contracts
  FooPlugin.cs               # : ITaruiPlugin, ConfigureCommands(builder)
  FooCommands.cs             # 静态 handler 方法
  FooScope.cs                # 可选:PathScope / 快捷键 scope 解析
  FooJsonContext.cs         # 子 JsonSerializerContext(注册插件专属 DTO)
  InternalsVisibleTo("Tarui.Plugins.Tests")  # 仅集成测试可见 internal
```

对应 NuGet 包 `Tarui.Plugins.Foo` 的元数据由 `Directory.Build.props` 统一写入;不要在 `csproj` 里覆写 `Version` / `Authors` / `PackageId` / `GenerateDocumentationFile` / `IncludeSymbols`。

`ITaruiPlugin` 契约:

```csharp
public interface ITaruiPlugin
{
    void ConfigureCommands(CommandRouterBuilder commands);
}
```

注册入口:

```csharp
public static IServiceCollection AddPlugin<TPlugin>(this IServiceCollection services)
    where TPlugin : class, ITaruiPlugin
    => services.AddSingleton<ITaruiPlugin, TPlugin>();
```

## 2. 现有插件清单

| 插件包 | 命令前缀 | 命令数 | 备注 |
| --- | --- | --- | --- |
| `Tarui.Plugins.Core` | `core:app|*` | 1 | 壳握手 `get-info` |
| `Tarui.Plugins.Window` | `core:window|*` | 24 | 窗口生命周期、几何、装饰、监视器 |
| `Tarui.Plugins.Webview` | `plugin:webview|*` | 5+ | 当前 Webview + 多 Webview 寻址 + DevTools / Eval / Download |
| `Tarui.Plugins.WindowState` | `plugin:window-state|*` | 3 | 持久化窗口尺寸/位置 |
| `Tarui.Plugins.Event` | `core:event|*` | 1 | `emit` |
| `Tarui.Plugins.Dialog` | `plugin:dialog|*` | 2+ | 文件/目录/消息/确认/ask |
| `Tarui.Plugins.System` | `core:path\|core:os\|core:process\|core:shell\|core:clipboard\|core:cli\|core:channel` | 14+ | 平台能力 |
| `Tarui.Plugins.FileSystem` | `plugin:fs|*` | 16 | 受 `PathScope` 限制 + 流式 |
| `Tarui.Plugins.Menu` | `plugin:menu|*` | 7 | 原生菜单 + 上下文菜单 |
| `Tarui.Plugins.Tray` | `plugin:tray|*` | 6 | 系统托盘 |
| `Tarui.Plugins.Notification` | `plugin:notification|*` | 4 | 系统通知 |
| `Tarui.Plugins.Autostart` | `plugin:autostart|*` | 3 | 开机自启 |
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

## 3. 新增插件流程

1. **DTO record + JSON 元数据**:在 `src/core/Tarui.Contracts/` 新增 record 并在 `TaruiJsonContext` 注册。
2. **插件项目**:在 `src/plugins/` 新建 `Tarui.Plugins.Foo/` 项目,`FooPlugin.cs` 实现 `ITaruiPlugin.ConfigureCommands(CommandRouterBuilder)`,`commands.Add("plugin:foo|do", argsTypeInfo, resultTypeInfo, handler, "plugin:foo|do")`。
3. **DI 扩展**:同项目 `FooPluginServiceCollectionExtensions.AddFooPlugin()`。
4. **能力清单**:在 `examples/demo/capabilities/*.json` 加入 `plugin:foo|do`(必要时细化 scope)。
5. **前端桥接**:`web/packages/api/src/foo.ts` + `package.json` 的 `exports` 添加 `"./foo"`;`web/packages/api/src/index.ts` barrel 导出。
6. **自测试**:`tests/Tarui.Plugins.Foo.Tests/FooPlugin.Tests.csproj`,控制台式自测试。
7. **架构门禁**:`dotnet run --project tests/Tarui.Architecture.Tests` 通过。
8. **文档同步**:在 [`docs/design/alignment-plan.md`](alignment-plan.md) §15 状态表登记,在 [`docs/usage/plugins.md`](../usage/plugins.md) 描述用法。

## 4. 权限命名与 Scope

- **命令 ID**:`<prefix>:<verb>|<action>`,前缀 `core`/`plugin`,动作小写、横线分隔,如 `plugin:fs|read-file-stream`。
- **权限 ID** 与命令 ID 一一对应;`RegisteredPermissions` 自动登记,无需手工维护。
- **事件 ID**:`<prefix>://<verb>`,前缀由插件或 shell 保留,Web 不可发起保留前缀。
- **Scope allow / deny**:FileSystem / GlobalShortcut / Store / PersistedScope / Http 等插件支持。deny 命中即拒绝,与 capability 静态授权合并时仍 deny 优先。

## 5. 第三方(out-of-tree)插件

### 5.1 接入流程(应用开发者)

1. `dotnet add package Tarui.Plugins.Foo`
2. `builder.Services.AddFooPlugin()`(组合根显式注册)
3. `pnpm add @lytree/plugin-foo`
4. `capabilities/main.json` 增加授权(如 `plugin:foo|*` 或逐命令 + scope)
5. `tarui build` 自动合成校验 schema

与 Tauri 的 Cargo.toml + capability 步骤数一致;差异仅在编译期注册形式(C# 扩展方法 vs Rust 宏),同等显式。

### 5.2 脚手架

`tarui plugin init <name>`(详见 [`cli-workflow.md`](cli-workflow.md) §W4)生成完整插件骨架:

```text
tarui-plugin-store/
  src/Tarui.Plugins.Store/        # csproj + Plugin.cs + Contracts.cs
  permissions/                    # schema.json + default.json + README.md
  guest-js/                       # @lytree/plugin-store: package.json + tsconfig + src/
  tests/Tarui.Plugins.Store.Tests/  # 自测试 csproj + Program.cs 骨架
  examples/demo/README.md         # 接线示例
  README.md                       # 使用 / 权限 / 威胁模型骨架
```

类名 / 方法名由插件名规范化推导(`store` → `StorePlugin` / `AddStorePlugin`),杜绝遗留占位符。`permissions/*.json` 同时设 `Link`(构建输出 `bin/permissions/store/`)与 `PackagePath`(nupkg 内 `permissions/store/`)。

### 5.3 预检与发布

`tarui plugin pack` 五步预检:

1. 布局检测(`src` 恰一个 csproj)
2. 权限一致性(`default.json` 引用必须声明于 `schema.json` 且 id 以 `plugin:` 开头、唯一)
3. 双包版本一致性(csproj `Version` == guest-js `package.json.version`)
4. 运行插件自测试
5. `dotnet pack`(确认 nupkg 含 `permissions/`)+ `npm pack`(guest-js)

发布:`dotnet nuget push` + `npm publish`(lockstep 版本)。

## 6. 官方插件审查清单

发布前必须自检:

- scope allow/deny 反向测试覆盖
- 事件路由授权测试
- 涉路径操作复用 `IFileAccessPolicy`
- 无反射 / 无扫描 / 无动态加载
- 默认最小权限(不自动授予 `default` permission,有意偏离 Tauri)
- README 威胁模型完整
- 能力矩阵与支持平台声明同步

应用侧 schema 启动校验(`SchemaSynthesizer`)是插件清单的运行时真源,**永不自动授予**任何权限。
