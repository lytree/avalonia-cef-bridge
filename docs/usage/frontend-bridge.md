# 前端桥接:`@lytree/api`

> `@lytree/api` 为每个插件契约提供强类型模块;主入口和子路径导出均支持。
>
> 配套文档:[`../design/ipc.md`](../design/ipc.md)、[`../dev/frontend-sdk.md`](../dev/frontend-sdk.md)、[`./plugins.md`](plugins.md)。

## 1. 安装与导入

```powershell
pnpm add @lytree/api
```

```ts
import { invoke } from "@lytree/api/ipc"
import { getAppInfo } from "@lytree/api/app"
import { getCurrentWindow } from "@lytree/api/window"
import { emit, listen } from "@lytree/api/event"
import { openDialog } from "@lytree/api/dialog"        // openExternal 也在 dialog barrel
import { openExternal } from "@lytree/api/shell"      // OS 默认处理器
import { fs, store, log } from "@lytree/api/fs"       // 命名空间风格
```

子路径 vs barrel:

- **barrel(`@lytree/api`)** —— 全部模块聚合,`open` 重命名为 `openDialog` / `openExternal` 避免冲突。
- **子路径(`@lytree/api/<name>`)** —— 单模块导出,保留 Tauri 风格的简短名。

## 2. Window 类

`Window.getCurrent()` 无 label 时指向当前 Webview 所在窗口,`getByLabel('editor')` 寻址其它窗口:

```ts
import { Window } from '@lytree/api/window';

const win = Window.getCurrent();
const editor = Window.getByLabel('editor');

// 状态
const state = await win.getState();

// 控制
await win.setTitle('Hello');
await win.close();  // 协作式:仅设置 close-pending;真正退出由 native 完成
win.denyClose();    // 取消强制关闭回退定时器

// 监听
const off = await win.onMoved(({ x, y }) => console.log('moved', x, y));
// off() 解除监听
```

生命周期订阅:`onMoved` / `onResized` / `onFocusChanged` / `onCloseRequested` / `onDestroyed`,统一包装 `listen`。

## 3. 错误码语义

所有调用失败以 `IpcCommandError` 抛出,携带路由器错误码:

| 错误码 | 含义 |
| --- | --- |
| `not_authorized` | 窗口 capability 未授予该命令 |
| `not_found` | 命令未注册或窗口不存在 |
| `invalid_payload` | DTO 校验失败 |
| `scope_denied` | scope allow/deny 命中拒绝 |
| `not_supported` | 当前平台不支持该能力 |
| `resource_conflict` | 系统占用或冲突 |
| `user_denied` | 用户拒绝系统授权 |
| `io_error` | 文件 / 网络 / 进程 I/O 错误 |

```ts
try {
  await fs.readTextFile({ path: 'documents/note.txt' });
} catch (err) {
  if (err.code === 'not_authorized') {
    console.error('窗口未授权');
  } else if (err.code === 'scope_denied') {
    console.error('scope 拒绝');
  } else {
    throw err;
  }
}
```

错误码是契约,跨版本稳定;**不应依赖错误文本**(本地化、漂移)。

## 4. Event

```ts
import { emit, listen } from '@lytree/api/event';

// Web 侧只能发 user:// 前缀
await emit('demo://echo', { msg: 'hello' });

// 监听(原生或自定义)
const off = await listen('demo://echo', (event) => {
  console.log(event.payload, event.id);
});
```

## 5. Channel(端到端流式 IPC)

```ts
import { invoke } from '@lytree/api/ipc';

// 流式读大文件
const channel = new Channel<{ chunk: string; done: boolean }>();
channel.onmessage = (frame) => {
  if (frame.done) channel.close();
  else console.log('chunk:', frame.chunk);
};
await invoke('plugin:fs|read-file-stream', { path: 'big.bin', channel });

// HTTP 流式响应
const httpChannel = new Channel<{ data: string; done: boolean }>();
httpChannel.onmessage = (frame) => { /* ... */ };
await invoke('plugin:http|fetch', {
  url: 'https://api.example.com/stream',
  stream: httpChannel,
});
```

Channel 背压由 `WebviewSession.SendAsync` await 天然成立:JS 调度满时,生产者自然减速。

## 6. 模块一览(30 个)

| 模块 | 子路径 | 风格 |
| --- | --- | --- |
| `ipc` | `@lytree/api/ipc` | invoke / Channel |
| `app` | `@lytree/api/app` | getAppInfo |
| `window` | `@lytree/api/window` | 类(Window) |
| `webview` | `@lytree/api/webview` | 类(Webview) |
| `event` | `@lytree/api/event` | emit / listen |
| `dialog` | `@lytree/api/dialog` | openDialog / openMessage / openAsk |
| `os` | `@lytree/api/os` | 函数式 |
| `path` | `@lytree/api/path` | 函数式 |
| `process` | `@lytree/api/process` | 函数式 |
| `shell` | `@lytree/api/shell` | openExternal |
| `clipboard` | `@lytree/api/clipboard` | readText / writeText / readHtml / writeHtml / readImage / writeImage |
| `cli` | `@lytree/api/cli` | parseCliArgs / getCliMatches |
| `channel` | `@lytree/api/channel` | Channel 类 |
| `platform` | `@lytree/api/platform` | capabilities |
| `fs` | `@lytree/api/fs` | namespace(fs.*) |
| `store` | `@lytree/api/store` | namespace(store.*) |
| `log` | `@lytree/api/log` | namespace(log.*) |
| `menu` | `@lytree/api/menu` | 类(Menu) |
| `tray` | `@lytree/api/tray` | 类(TrayIcon) |
| `notification` | `@lytree/api/notification` | 类(Notification) |
| `autostart` | `@lytree/api/autostart` | 函数式 |
| `global-shortcut` | `@lytree/api/global-shortcut` | 函数式 |
| `window-state` | `@lytree/api/window-state` | 函数式 |
| `single-instance` | `@lytree/api/single-instance` | 函数式(事件 helper) |
| `deep-link` | `@lytree/api/deep-link` | getCurrent / onOpenUrl |
| `updater` | `@lytree/api/updater` | 类(Updater) |
| `http` | `@lytree/api/http` | 类(HTTP) |
| `websocket` | `@lytree/api/websocket` | 类(TaruiWebSocket) |
| `cookie` | `@lytree/api/cookie` | 函数式 |
| `positioner` | `@lytree/api/positioner` | 函数式 |
| `persisted-scope` | `@lytree/api/persisted-scope` | allowScope / denyScope / resetScope |

完整类型与函数签名见 `web/packages/api/src/<name>.ts`。

## 7. Demo 实战

Demo 前端(`examples/demo/web/src/App.tsx`)演示:

- `getAppInfo()` 拿壳握手元数据。
- `getCurrentWindow().getState()` 轮询窗口状态;`onMoved` / `onResized` / `onFocusChanged` 订阅变化。
- `store.set` / `get` / `keys` 操作 `appData/settings.json`。
- `fs.readDir({ base: 'appData' })` 列出隔离目录。
- `emit('demo://echo', payload)` 触发路由事件。
- Channel 流式读 / 写 + Shell stdio 流 + HTTP 流。
