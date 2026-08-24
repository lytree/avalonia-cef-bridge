# tarui.net 当前项目优化审计

审计日期：2026-08-24  
审计范围：`src/`、`tests/`、`web/`、`examples/demo/`、`eng/`、`.github/workflows/`、解决方案与包配置  
审计方式：静态代码审查、跨模块契约比对、Release 构建、全量自测试、前端 lint/build、NuGet/npm 打包检查  
变更边界：除本文档外，未修改生产代码、测试或配置。

## 1. 执行摘要

项目当前基线总体健康：Release 构建为 0 警告/0 错误，21 个控制台自测试项目全部通过，主 Web 工作区 lint/build、demo Web build、NuGet pack 与 npm pack dry-run 均成功。无反射 IPC、显式插件注册、源码生成 JSON 元数据、能力清单与架构门禁等方向是正确的。

但审计确认仍有若干会影响安全边界、跨应用隔离、发布可靠性和并发数据一致性的优化项。最优先的问题不是代码风格，而是以下四类系统性风险：

1. 默认分支为 `master`，CI 仅监听 `main`，当前 PR/Push 门禁可能根本不触发。
2. 能力模型在创建子窗口时只比较 permission ID，不比较 scope 与 events，可产生权限提升。
3. 文件系统、Tray 和路径解析存在多个独立的授权边界缺口。
4. CLI 生成的 `latest.json` 与运行时 Updater 契约不兼容，当前发布产物无法被已实现的 Updater 验证使用。

建议先完成 P0 和 P1 中的安全、发布、隔离整改，再继续扩大插件和平台能力。

## 2. 审计基线

| 项目 | 结果 |
| --- | --- |
| Git 工作区 | 审计开始时 clean；默认分支及远端 HEAD 均为 `master` |
| .NET SDK | `10.0.400`，与 `global.json` 一致 |
| 解决方案 | 56 个可 restore 项目，含 21 个 `*.Tests` 自测试项目 |
| Release build | 0 warnings，0 errors |
| 全量自测试 | 21/21 通过；Architecture gate 扫描 859 个 active files |
| 主 Web lint | 通过 |
| 主 Web build | 通过 |
| demo Web build | 通过 |
| NuGet pack | 通过，生产包与符号包均生成 |
| npm pack dry-run | 通过，包内容可生成 |

本次未执行真实桌面 CEF 交互、GitHub Release 推送、NuGet/npm 实际发布、签名更新端到端、macOS/Linux 平台运行测试。因此涉及这些环境的结论基于确定的代码路径与配置行为，实施后仍需平台验证。

## 3. 优先级定义

| 优先级 | 含义 |
| --- | --- |
| P0 | 安全边界失效、CI/发布阻断、核心功能当前不可用；应在下一次发布前完成 |
| P1 | 高概率数据错误、跨应用冲突、资源耗尽或关键质量门禁缺失；应进入最近迭代 |
| P2 | 生命周期、兼容性、可维护性和工程效率问题；应纳入后续硬化 |

## 4. P0：发布前必须处理

### P0-01 CI 未监听仓库默认分支

**证据**

- 仓库本地分支和远端 HEAD 均为 `master`。
- `.github/workflows/ci.yml:6-10` 仅监听 `push/pull_request` 的 `main`。

**影响**

向当前默认分支提交或发起 PR 时，构建、测试、打包和 Web 门禁可能完全不运行。后续所有测试改进在触发条件修复前都不能形成保护。

**建议**

- 将 CI 分支改为 `master`，或先完成默认分支迁移再统一改为 `main`。
- 增加 `workflow_dispatch` 便于手动验证。
- 在仓库设置中把 CI job 配为 required status check，并确认保护规则指向真实默认分支。

### P0-02 创建子窗口可绕过 scope 与事件授权

**证据**

- `src/desktop/Tarui.Shell/WindowCapabilityResolver.cs:39-53` 只验证目标窗口的 permission ID 是否为调用方 permission ID 的子集。
- `CapabilitySet` 还包含 permission scopes 和 reserved events，但这里未比较。

**影响**

调用窗口拥有某个命令的窄 scope 时，可以创建同 permission ID、但 scope 更宽或 reserved events 更多的预声明窗口。例如调用方只允许 `appData/public/**`，目标窗口可配置为同一读命令但允许 `home/**`。这破坏了“创建窗口不能提权”的核心不变量。

**建议**

- 将授权比较升级为完整 capability 子集判断：permissions、events、allow、deny 全部参与。
- 目标 allow 必须被调用方 allow 覆盖；目标 deny 不得弱于调用方 deny。
- 通配 permission `*`、空 allow 的“默认全允许”语义要显式建模，避免简单集合比较误判。
- 新增 scope 扩大、deny 缩小、reserved event 增加三个反向测试。

### P0-03 文件与路径授权边界存在多处绕过

这是多条独立路径，建议作为一次安全硬化统一修复。

**证据 A：Windows deny 可被大小写绕过**

- `src/plugins/Tarui.Plugins.FileSystem/FileSystemPlugin.cs:286-308` 使用固定 `StringComparison.Ordinal` 匹配 scope glob。
- `src/plugins/Tarui.Plugins.Store/StorePlugin.cs:190-212` 存在同样实现。
- Windows 文件系统通常大小写不敏感，`documents/secrets/*` 与 `Documents/Secrets/key.json` 可指向同一文件，但 deny 不会命中。

**证据 B：递归 read-dir 可沿链接越界**

- `src/plugins/Tarui.Plugins.FileSystem/FileSystemService.cs:183-203` 只授权入口目录，递归过程中直接遍历 `Directory.GetDirectories`。
- 新遇到的符号链接或目录联接没有重新授权，也没有 visited、最大深度或最大条目数。

**证据 C：Tray 图标可读取任意绝对路径或 UNC**

- `src/desktop/Tarui.Shell/AvaloniaTrayService.cs:211-227,272-289` 允许 rooted path，并直接交给 `Bitmap`。
- `base:relative` 只是 `Path.Combine`，没有 containment 或 link 检查；`resources:../../...` 可逃逸，UNC 还可能触发 SMB 访问。

**证据 D：path.resolve 存在兄弟目录前缀逃逸**

- `src/plugins/Tarui.Plugins.System/PathService.cs:28-35` 用 `StartsWith(root)` 判断包含关系。
- 已复现：根为 `.../tarui.net` 时，`../tarui.net-shadow/secret.txt` 规范化后仍通过现有前缀检查。

**影响**

Web renderer 可能越过声明的 allow/deny scope，枚举授权根外目录，读取任意图标路径，或获得根外绝对路径。Tray 的 UNC 路径还可能造成不期望的网络访问和凭据协商。

**建议**

- 抽取唯一的跨平台路径 glob 与 containment 实现，Windows 使用 `OrdinalIgnoreCase`，Unix 使用 `Ordinal`。
- 复用 `Path.GetRelativePath`/`IFileAccessPolicy`，不要使用字符串前缀判断。
- recursive read-dir 逐项重新授权，默认不跟随链接，并增加 visited、深度、条目数和 cancellation 限制。
- Tray 禁止裸绝对路径与 UNC；通过 `IFileAccessPolicy` 和显式 scoped read permission 加载。
- 增加 Windows 大小写、兄弟目录同前缀、symlink/junction、UNC 与循环链接测试。

### P0-04 发布清单与 Updater 契约不兼容

**证据**

- `src/tarui-cli/LatestManifestDto.cs:10-18` 生成 `version/url/sha256:string/signature`。
- `src/tarui-cli/BuildCommand.cs:177-185` 写出的 signature 固定为空。
- `src/core/Tarui.Contracts/UpdateContracts.cs:21-26` 运行时要求 `schemaVersion/version/files/sha256:map/signature`。
- `src/desktop/Tarui.Shell/UpdateVerifier.cs:41-63` 会拒绝空 signature 和不匹配结构。

**影响**

CLI 输出的 `latest.json` 无法被当前 Updater 解析和验证，发布产物与运行时更新功能是断开的。现有构建、单元自测试和 pack 门禁不会发现这个端到端不兼容。

**建议**

- CLI 与 Updater 复用同一个 `UpdateManifest` 契约和 canonicalization 实现。
- 发布阶段由隔离私钥对 schema/version/files/hash/size 签名，不在仓库或客户端保存私钥。
- 将文件长度加入签名清单，下载时同时校验 size 和 hash。
- 增加“CLI 产出 -> UpdateVerifier 验证 -> Updater 下载”的端到端自测试。

## 5. P1：最近迭代处理

### P1-01 缺少统一应用身份，多个应用共享运行时资源

**证据**

- `src/core/Tarui.Ipc/FileAccessPolicy.cs:96,335-363` 的 app data 根固定为 `tarui.net`。
- `src/plugins/Tarui.Plugins.System/PathService.cs:12,40-68` 使用相同固定名。
- `src/desktop/Tarui.Shell/TaruiShellServiceCollectionExtensions.cs:59-63` 的 window-state 固定落在 `tarui.net/window-state`。
- `src/desktop/Tarui.Shell/UpdaterConfiguration.cs:55-62` 的 updater staging 固定落在 `tarui.net/updater/staging`。
- `src/plugins/Tarui.Plugins.Core/CorePlugin.cs:25-30` 的 product/version 也固定为框架值，而非应用值。

**影响**

两个基于 tarui.net 构建的应用会共享 Store、FS、窗口状态、日志与更新 staging。已知相对路径可能造成跨应用读写、状态覆盖和更新目录互相清理；Web handshake 还会报告错误的产品身份。

**建议**

- 引入单一 `TaruiApplicationIdentity`，至少包含 product name、identifier、version。
- 由 Hosting/manifest 注入，FileAccessPolicy、PathService、window-state、updater、single-instance、deep-link 和 handshake 统一消费。
- OS 路径使用经过验证/规范化或哈希的稳定 identifier，而不是显示名称。
- 增加两个不同 identity 的隔离集成测试。

### P1-02 单实例通信端点没有包含 ApplicationId

**证据**

- `src/desktop/Tarui.SingleInstance/SingleInstanceIdentity.cs:9-15` 的锁名包含 `ApplicationId`，但 pipe/socket 只使用 `ChannelName`。
- 模板和 demo 都将 `SingleInstanceChannel` 设为通用值 `main`：`src/templates/Tarui.Templates/MyApp.Desktop/Program.cs:13-14`、`examples/demo/Demo.Desktop/Program.cs:28-29`。
- 服务端直接使用该 channel：`SingleInstanceCoordinator.cs:132-173`。

**影响**

不同应用可以分别获得自己的锁并成为 primary，却竞争同一个通信端点。Windows 上 second activation 可能送到错误应用；Unix 上后启动应用会删除同名 socket 路径，使先启动应用失去可达端点。

**建议**

- pipe/socket 名由 `ApplicationId + ChannelName + user/session` 派生，并进行长度限制和稳定哈希。
- Unix socket 放在权限受控的 per-user runtime directory，避免共享 temp 根。
- 增加“不同 ApplicationId、相同 ChannelName”双进程测试。

### P1-03 CapabilityLoader 合并与平台语义不完整

**证据**

- `src/desktop/Tarui.Shell/CapabilityLoader.cs:55-58,90-105` 只验证 `platforms` 字段值，不按当前 OS 过滤授权。
- `CapabilityLoader.cs:29,73-80` 对跨 manifest 同名 scoped permission 使用无序文件枚举和覆盖赋值，后加载项可能丢掉已有 deny。
- `CapabilityLoader.cs:61-87` 将 events 合并放在 permission 循环内；`permissions: []` 时不会创建 window bucket，也不会授予 events。

**影响**

Linux-only capability 会在 Windows 生效；同一窗口的 scope 结果依赖文件枚举顺序；receive-only capability 被错误视为不存在。前两项会扩大授权，第三项会让合法配置失效。

**建议**

- 在读取 manifest 后先执行平台过滤，再合并 windows、events、permissions。
- 对跨 manifest 同名 scoped permission 采用确定规则：拒绝重复，或 allow/deny 明确定义合并语义且 deny 取并集。
- 对文件名排序不能替代安全合并，只能提升可复现性。
- 增加 platform 过滤、顺序反转、event-only manifest 测试。

### P1-04 CI/Release 仅执行 6/21 个自测试项目

**证据**

- `.github/workflows/ci.yml:105-112` 和 `release.yml:112-119` 手工列出 6 个测试项目。
- 当前仓库实际有 21 个 `*.Tests` 项目，15 个未进入门禁，包括 Capabilities、FileSystem、Store、Updater、SingleInstance、DeepLink、Notification 等。

**影响**

权限、签名、文件系统和平台插件回归可以在 CI 绿灯下合并并直接发布。手工列表还会随着新增测试项目继续漂移。

**建议**

- 新增 `eng/test-all.ps1`，自动枚举并顺序运行 `tests/*.Tests/*.csproj`。
- 脚本输出发现数、通过数、失败项目，并在发现数异常下降时失败。
- CI 和 Release 共用同一脚本，避免复制列表。
- 将 Windows/Linux 加入测试矩阵；桌面关键路径再补 macOS。

### P1-05 Release 版本、产物和发布动作缺少原子性

**证据**

- `.github/workflows/release.yml:49-66` 解析 tag/input 后，没有让该版本驱动 pack/build。
- `release.yml:121-127` 只比较 `TaruiVersion` 与 npm version，不比较 tag、demo manifest、CLI、模板和运行时 handshake。
- `release.yml:129-272` 并行执行 NuGet、npm 与安装器；部分成功后重试并不幂等。
- `release.yml:299-303` 将 `dist/*` 全部传给 `gh release create`，但 `src/tarui-cli/BuildCommand.cs:81-84` 固定创建 `dist/bin` 目录，最终附件步骤可能把目录当作文件处理并失败。

**影响**

推送 `tarui-v0.2.0` 时可能仍发布 0.1.0 包，却创建 0.2.0 GitHub Release；安装器失败时 npm/NuGet 可能已经发布，重跑又会遇到已有版本。最终 Release 还可能在所有注册表发布完成后才失败。

**建议**

- validate 第一阶段严格比较 tag/input、TaruiVersion、npm、demo manifest、CLI、模板与 handshake 版本。
- 先构建、签名、验证全部不可变产物，再开始任何外部 publish。
- GitHub Release 只上传明确的 `*.zip`、`*.msix`、`latest.json`、`*.nupkg`，不使用目录通配。
- npm/NuGet 发布前检查已有版本及产物 hash，使同一 commit 重跑可判定为成功或安全跳过。

### P1-06 Store 并发持久化可能丢失新数据

**证据**

- `src/plugins/Tarui.Plugins.Store/JsonStoreService.cs:33-49,62-82` 在锁内修改内存字典，随后离开锁异步写盘。
- `JsonStoreService.cs:139-149` 再次取快照并独立执行原子写。

**影响**

两个并发 mutation 可产生两个快照，较旧快照若最后完成会覆盖较新文件。内存仍保留新值，但进程重启后数据丢失；写盘失败时内存 mutation 也不会回滚。

**建议**

- 按 canonical store path 使用异步锁或单写队列，串行化 mutation、snapshot、persist。
- 明确写失败语义：回滚内存、保留 dirty 状态重试，或采用版本号防止旧快照提交。
- 增加可控延迟 writer 的并发顺序测试和重启重载测试。

### P1-07 Web IPC 请求可能永久悬挂并泄漏 pending

**证据**

- `web/packages/api/ipc.ts:91-95` 先将 Promise 写入全局 Map，再调用 `JSON.stringify` 和 native bridge。
- bridge 同步异常时没有删除 Map 项；native 响应丢失时没有 timeout 或 `AbortSignal`。
- `src/desktop/Tarui.Shell/WebviewSession.cs:210-219` 的事件入口吞掉全部异常，无法保证 pending 得到失败响应。

**影响**

调用 Promise 可永久 pending，Map 持续增长，界面操作进入不可恢复等待。窗口关闭、导航或 bridge 故障时，所有未完成请求都没有统一清理。

**建议**

- `invoke` 支持默认超时和可选 `AbortSignal`。
- bridge 调用放入 `try/catch/finally`，任何同步错误都删除 pending。
- 页面卸载/bridge 重置时批量 reject 未完成调用。
- 增加 bridge 抛错、无响应、迟到响应、取消和窗口关闭测试。

### P1-08 Updater 缺少大小限制和并发事务隔离

**证据**

- `src/desktop/Tarui.Shell/UpdaterService.cs:137-144` 使用 `GetByteArrayAsync` 无界缓冲 manifest 后才验签。
- `UpdaterService.cs:195-208` 下载文件直到 EOF，没有单文件/总量限制。
- `UpdaterService.cs:80-115` 每个 download 调用都会清理同一 staging，并使用固定 `target + .tmp`。
- `UpdaterConfiguration.cs:39-45` 默认允许 HTTP manifest。

**影响**

远端故障或攻击流量可在签名失败前耗尽内存/磁盘；两个并发下载会互相删除、覆盖或混合 staging，一方可能报告成功后结果又被另一方改变。

**建议**

- 默认强制 HTTPS；如允许 HTTP，必须显式 opt-in。
- 对 manifest、单文件、总下载量设硬上限，并校验 Content-Length 与签名 size。
- 使用 `SemaphoreSlim` 或事务目录串行/隔离下载，全部验证后原子切换 active staging。
- 增加超大/无限流、并发下载、取消、stale staging 测试。

### P1-09 Relaunch 与单实例生命周期存在竞态

**证据**

- `src/desktop/Tarui.Hosting/HostAppShutdown.cs:20-29` 在旧进程释放单实例锁前直接启动新进程。
- `SingleInstanceGuard.cs:53-64` 中新进程若看到旧锁，会作为 secondary 转发并退出，随后旧进程也退出。
- `SingleInstanceCoordinator.cs:58-76` 检查 main window 与入队不是同一原子状态；`MainWindowLauncher.cs:30-35` 可能在中间完成空 Flush，造成 activation 永久滞留。
- Unix listener 在 `SingleInstanceCoordinator.cs:175-195` 使用不可取消的同步 `Accept()`；Dispose 只等待两秒。

**影响**

relaunch 可能表现为应用彻底退出；启动期第二实例参数可能丢失；Unix 停止后后台 listener 和 socket 可能残留。

**建议**

- relaunch 使用父子握手：旧实例完成 host stop/锁释放后，新实例再竞争 primary。
- coordinator 维护原子 ready 状态，在同一锁中决定直接投递或入队。
- Unix 改用 `AcceptAsync(token)`，或 Dispose 时关闭 listener 解除阻塞。
- 增加真实双进程 relaunch、barrier 控制入队、Unix socket 清理测试。

### P1-10 CEF 集成边界需要强化生命周期与资源释放

**证据**

- `src/webview/CefGlue.Next.Avalonia/CefGlueNextAvaloniaResourceHandler.cs:11-46` 允许 `ResourceProvider.Resolve` 异常直接越过 native callback 边界。
- `CefGlueNextAvaloniaRuntime.cs:32-44` 在 CEF 已初始化时直接返回，不校验第二份配置是否一致；custom scheme 等配置可能静默失效。
- `CefGlueNextAvaloniaWebView.cs:369-387` 的 `CefBeforeDownloadCallback` 没有确定性 Dispose。
- 同一控件的 `Source` 只在构造和显式 Navigate 时更新，redirect/页面内导航后的状态可能陈旧。

**影响**

provider 异常可能导致 native 请求回调失败甚至进程不稳定；初始化顺序错误会让 scheme 永久未注册；大量下载请求积压 native 引用；`webview|get-state` 返回旧 URL。

**建议**

- native callback 边界捕获 provider 异常，记录受控日志并返回固定 500。
- 保存运行时配置指纹，重复初始化只允许完全等价配置，否则明确抛错。
- 对 callback 使用 `using` 或明确所有权转移。
- 订阅主框架 address change，更新 Source，并在 Close 时解除订阅。
- 增加 throwing provider、冲突初始化、callback disposal、redirect 状态测试。

### P1-11 TypeScript 契约和 demo 行为已与宿主漂移

**证据**

- `web/packages/api/fs.ts:3-29` 暴露大量宿主不支持或拼写不同的 base，例如 `resource/picture/font`；宿主集合见 `FileAccessPolicy.cs:335-352`。
- `fs.ts:53-85` 的 `overwrite/path/isFile/children/*AtMs` 与 `src/core/Tarui.Contracts/FileSystemContracts.cs:18-37` 不一致。
- demo 使用 `demo://echo`、主 Web 使用 `demo://ping`，但 `src/core/Tarui.Ipc/EventNames.cs:63-68` 只允许 Web 发出 `user://`。
- demo 默认文件为 `demo/hello.txt`，而 `examples/demo/capabilities/main.json:192-209` 只允许 `documents/**`。

**影响**

TypeScript 编译通过的调用会在运行时失败或返回与声明不同的数据；仓库内两个示例的事件按钮稳定得到 `EVENT_NOT_ALLOWED`；demo 默认 FS 读写也会被 scope 拒绝。

**建议**

- 从 C# source-generated contract 或共享 schema 生成 TypeScript DTO 和枚举，避免手工双写。
- 建立跨语言 JSON snapshot/type test，覆盖每个公开命令的请求与响应。
- `emit` 参数收窄为模板字面量 ``user://${string}``。
- 修正 demo 默认路径并加入 capability + IPC + Web API 冒烟测试。

## 6. P2：工程硬化与可维护性

### P2-01 首次运行时 writable base 不能自举创建

- `src/core/Tarui.Ipc/FileAccessPolicy.cs:105-117` 只有目录已存在才返回 appData/appConfig/appCache/appLog base。
- 首次安装时调用方会在执行 `Directory.CreateDirectory` 前就得到“base 不可用”。
- 建议对已知 writable base 返回规范路径，在 write 授权阶段安全创建；resources 等只读 base 仍要求存在。

### P2-02 IPC 协议版本和错误边界不完整

- `InvokeRequest.Protocol` 在 `IpcDispatcher.cs:15-17`、`CommandRouter.cs:68-85` 未校验。
- payload 的 `JsonException` 可能落到 `CommandRouter.cs:115-117`，返回通用 `COMMAND_FAILED` 和原始异常 message。
- 建议增加 `UNSUPPORTED_PROTOCOL`、稳定的 `INVALID_ARGUMENTS`，通用异常只内部记录并向 Web 返回固定无敏感信息文案。

### P2-03 RemoteLogSink 无界且停止不等待 pump

- `src/desktop/Tarui.Shell/RemoteLogSink.cs:17-28` 使用 unbounded channel。
- `RemoteLogSink.cs:30-46` Dispose 只 complete 并读一项，不等待 `_pump`。
- 建议使用 bounded channel、drop/coalesce 策略、丢弃计数指标和 `IAsyncDisposable` 停止流程。

### P2-04 Windows autostart 参数引用不符合 argv 规则

- `src/plugins/Tarui.Plugins.Autostart/AutostartConfig.cs:61-79` 仅将 `"` 替换为 `\"`，没有处理闭引号前反斜杠，且显式允许 CR/LF。
- 建议实现标准 Windows command-line quoting，并用 `CommandLineToArgvW` 往返测试覆盖空参数、尾随反斜杠、嵌入引号和控制字符。

### P2-05 Web SDK 缺少独立测试，ESM 兼容范围不明确

- `web/packages/api/package.json` 没有 test script，CI lint 也只覆盖 `@lytree/web`。
- 包声明 ESM，但源码使用无扩展相对导入；原生 Node ESM 解析 `dist/index.js` 时无法解析 `./ipc`，当前实际依赖 bundler 行为。
- 建议加入 Vitest/node bridge mock，覆盖 invoke/listen/error/timeout/unlisten；使用 NodeNext + `.js` 导入或输出 bundle，并明确支持环境。
- 对 move/resize 触发的多次 IPC 做 throttle、请求合并和过期响应保护。

### P2-06 源码生成命令目录与运行时注册存在漂移风险

- `src/generators/Tarui.Ipc.Generators/TaruiCommandCatalogGenerator.cs:13-22` 只收集 `[TaruiCommand]`。
- 多个插件通过 `commands.Add` 注册命令但未为处理器标注 attribute，生成目录不能代表完整运行时 router。
- 建议统一命令声明源，并增加 `GeneratedCommandCatalog.Commands` 与实际 router commands 的集合一致性门禁；生成器还应对空命令和重复命令发出诊断。

### P2-07 供应链与平台门禁可进一步增强

- `eng/cef/install-runtime.ps1` 从同一远端下载 archive 和 `.sha1` sidecar，只能发现传输损坏，不能形成仓库级固定信任。
- CI .NET job 仅运行 Ubuntu，Windows 安装器 job 不运行测试；实际项目包含大量 Windows/CEF/注册表/命名管道行为。
- `global.json` 固定 `10.0.400`，workflow 使用浮动 `10.0.x`，本地与 CI 解析结果可能漂移。
- 建议提交按 RID 固定的 SHA-256 清单或验证上游签名；使用 Windows/Linux 测试矩阵；setup-dotnet 直接读取 `global.json`。

## 7. 其他确定性优化项

以下问题优先级低于上述安全与发布项，但适合随相关模块整改一并处理：

| 模块 | 优化项 | 证据/方向 |
| --- | --- | --- |
| FileSystem | `copy-file` 绕过 MaxReadBytes/MaxWriteBytes/累计预算，并使用同步 `File.Copy` | `FileSystemService.cs:115-125`；改为可取消的异步流复制、预算预留和原子目标替换 |
| Hosting | Avalonia lifetime 注册前的 shutdown 请求可能丢失 | `TaruiLifetimeBridge.cs:12-20`；Register 时检查已请求状态并立即 Shutdown |
| SingleInstance | queued activation 会对 `ISecondActivationSink` 重复通知 | `SingleInstanceCoordinator.cs:58-60,79-84`；明确 sink 是接收时通知还是 flush 时通知，避免两次调用 |
| Window state | 文件写入为同步且非原子，CancellationToken 未使用 | `JsonWindowStateStore.cs:13-43`；采用异步原子写并处理损坏文件 |
| Core info | product/version/platform 为硬编码或低层枚举字符串 | `CorePlugin.cs:25-30`；由 ApplicationIdentity 和 RuntimeInformation 提供 |
| CEF cache | cache 使用进程 ID 路径，退出后缺少统一清理策略 | `CefGlueNextWebViewFactory.cs:24-28`；建立保留期与启动清理 |

## 8. 推荐实施路线

### 阶段 A：恢复门禁与发布可用性，预计 1-2 天

1. 修正 CI 默认分支触发。
2. 引入全量测试脚本并让 CI/Release 共用。
3. 统一 CLI latest.json 与 Updater 契约，增加端到端测试。
4. 增加发布 tag/version 强校验，限制 GitHub Release 附件列表。
5. 修正 demo 的 `user://` 事件和默认 FS 路径。

### 阶段 B：安全边界与应用隔离，预计 3-5 天

1. 完整比较子窗口 capability scopes/events。
2. 修复 scope 大小写、递归 symlink、Tray 路径和 path.resolve containment。
3. 引入统一 ApplicationIdentity，迁移所有 app-scoped OS 资源。
4. 重构 single-instance endpoint 命名和 Unix per-user socket 路径。
5. 修复 CapabilityLoader 平台过滤与确定性合并。

### 阶段 C：并发与生命周期，预计 3-5 天

1. Store 单写事务化。
2. Updater 大小限制、事务 staging 和并发互斥。
3. IPC timeout/cancellation/unload cleanup。
4. single-instance/relaunch/shutdown 生命周期竞态整改。
5. CEF 初始化、callback、provider exception 和 Source 同步整改。

### 阶段 D：契约与工程效率，预计 2-4 天

1. C# -> TypeScript 契约生成或 schema snapshot 门禁。
2. Web SDK 行为测试和 ESM 输出策略。
3. GeneratedCommandCatalog 与 router 一致性门禁。
4. Windows/Linux CI 矩阵、CEF SHA-256 固定信任与真机 smoke。

## 9. 建议新增的核心回归测试

| 测试主题 | 最小验证场景 |
| --- | --- |
| Capability 子集 | 相同 permission ID，目标扩大 allow、缩小 deny、增加 reserved event 均被拒绝 |
| Capability loader | linux-only 不在 Windows 生效；跨文件 scope 顺序反转结果一致；event-only 可加载 |
| 路径安全 | Windows 大小写 deny、兄弟目录前缀、symlink/junction 越界、UNC Tray 路径 |
| App identity | 两个 identifier 的 Store/FS/window-state/updater 路径互不相交 |
| Single instance | 不同 ApplicationId + 相同 ChannelName 同时运行；Unix Dispose 删除 socket |
| Store 并发 | 两次乱序完成写盘后重启仍保留最新完整状态 |
| Updater E2E | CLI 清单可验签、可下载；超大流被中止；并发调用 staging 不混合 |
| IPC Web | bridge 抛错、timeout、AbortSignal、页面卸载、迟到 response 均清理 pending |
| CEF | provider 抛错返回 500；冲突初始化失败明确；download callback 释放；redirect 更新 Source |
| 发布 | tag 与所有版本源不一致时在任何 publish 前失败；附件列表不含目录 |
| 跨语言契约 | 每个 TS DTO 的序列化 JSON 与 C# source-generated DTO snapshot 一致 |

## 10. 验证记录

已执行并通过：

```powershell
dotnet restore tarui.net.sln --configfile NuGet.Config
dotnet build tarui.net.sln -c Release --no-restore --nologo

# 自动枚举并运行 tests 下全部 21 个 *.csproj
dotnet run --project <each-test-project> -c Release --no-build

pnpm lint                       # web/
pnpm build                      # web/
pnpm build                      # examples/demo/web/
dotnet pack tarui.net.sln -c Release --no-build --nologo -o artifacts/audit-nuget
pnpm --filter @lytree/api pack --dry-run
```

验证结果：

- .NET Release build：0 warnings，0 errors。
- 自测试：21/21 通过。
- Architecture gate：通过，扫描 859 个 active files。
- 主 Web lint/build：通过。
- demo Web build：通过。
- NuGet/npm 打包检查：通过。

## 11. 结论

当前项目并非“整体质量差”，相反，它已经建立了较强的编译纪律、架构门禁、显式注册和安全意图。主要问题是功能增长速度已经超过了跨模块不变量的自动验证速度：应用身份、能力合并、TS/C# 契约、发布产物、Updater 和 single-instance 分别演进，却缺少端到端一致性门禁。

短期最有收益的策略不是继续增加插件，而是先把 P0/P1 项固化为自动测试与共享契约。完成阶段 A 和 B 后，项目的发布可信度与安全边界会有明显提升；完成阶段 C 和 D 后，后续功能扩展的回归成本会显著下降。
