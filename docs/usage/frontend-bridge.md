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
await win.close();

// 图标:PNG 字节,或恢复可执行文件内嵌图标(对标 Tauri `Image::from_app_icon_resource`)
await win.setIcon(pngBytes);   // null 清除图标
await win.restoreAppIcon();    // Windows;其它平台抛 NOT_SUPPORTED

// 监听
const off = await win.onMoved(({ x, y }) => console.log('moved', x, y));
// off() 解除监听
```

生命周期订阅:`onMoved` / `onResized` / `onFocusChanged` / `onDestroyed`,统一包装 `listen`。

## 3. 错误码语义

所有调用失败以 `IpcCommandError` 抛出,携带路由器错误码:

| 错误码 | 含义 |
| --- | --- |
| `PERMISSION_DENIED` | 窗口 capability 未授予该命令 |
| `COMMAND_NOT_FOUND` | 命令未注册或窗口不存在 |
| `INVALID_MESSAGE` | DTO 校验失败 |
| `PATH_DENIED` | scope allow/deny 命中拒绝 |
| `NOT_SUPPORTED` | 当前平台不支持该能力 |
| `RESOURCE_CONFLICT` | 系统占用或冲突 |
| `USER_DENIED` | 用户拒绝系统授权 |
| `IO_ERROR` | 文件 / 网络 / 进程 I/O 错误 |

```ts
try {
  await fs.readTextFile({ path: 'documents/note.txt' });
} catch (err) {
  if (err.code === 'PERMISSION_DENIED') {
    console.error('窗口未授权');
  } else if (err.code === 'PATH_DENIED') {
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

Channel 具备消费侧背压(`channel-credit-v1`):原生侧在途帧上限 16,前端 `onmessage`(可为异步)落定后回发 ACK 归还信用;未握手的老前端保持 fire-and-forget。协议细节见 `docs/design/ipc.md` §2。

## 6. 模块一览(31 个)

| 模块 | 子路径 | 风格 |
| --- | --- | --- |
| `ipc` | `@lytree/api/ipc` | invoke / Channel |
| `app` | `@lytree/api/app` | getAppInfo |
| `window` | `@lytree/api/window` | 类(Window) |
| `webview` | `@lytree/api/webview` | 类(Webview) / runtimeInfo |
| `event` | `@lytree/api/event` | emit / listen |
| `dialog` | `@lytree/api/dialog` | openDialog / openMessage / openAsk |
| `os` | `@lytree/api/os` | 函数式 |
| `path` | `@lytree/api/path` | 函数式 |
| `asset` | `@lytree/api/asset` | convertFileSrc |
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
| `persisted-scope` | `@lytree/api/persisted-scope` | allowScope / denyScope / resetScope |

完整类型与函数签名见 `web/packages/api/src/<name>.ts`。

### `convertFileSrc`

把本地绝对路径转换为受限 `asset://` URL,用于 `<img>` / `<video>` / `@font-face` 等浏览器原生加载路径(对标 Tauri `convertFileSrc`):

```ts
import { convertFileSrc } from '@lytree/api/asset'

const src = convertFileSrc('C:\\Users\\me\\Pictures\\a.png')
// asset://localhost/C%3A%5CUsers%5Cme%5CPictures%5Ca.png
document.querySelector('img')!.src = src
```

只有位于 `Tarui:Web:Asset:Roots` 白名单目录内的文件可被读取(逐 root 做包含检查并复用 `IFileAccessPolicy`),仅支持 GET/HEAD,超过 `Tarui:Web:Asset:MaxAssetBytes`(默认 64 MiB)返回 413。未配置任何 root 时 `asset` scheme 不会注册,需要在应用配置中显式开启;默认 CSP 已放行 `asset:`。

## 7. Demo 实战

Demo 前端(`examples/demo/web/src/App.tsx`)演示:

- `getAppInfo()` 拿壳握手元数据。
- `getCurrentWindow().getState()` 轮询窗口状态;`onMoved` / `onResized` / `onFocusChanged` 订阅变化。
- `store.set` / `get` / `keys` 操作 `appData/settings.json`。
- `fs.readDir({ base: 'appData' })` 列出隔离目录。
- `emit('demo://echo', payload)` 触发路由事件。
- Channel 流式读 / 写 + Shell stdio 流 + HTTP 流。

## 8. Webview 与运行时身份

`Webview` 类寻址单个浏览器表面:无 label 时指向当前渲染器所在 webview,`getByLabel('editor')` 寻址其它 webview(当前与宿主窗口共享 label)。

```ts
import { Webview, runtimeInfo } from '@lytree/api/webview';

const wv = Webview.getCurrent();
await wv.navigate('tarui://localhost/settings');
const state = await wv.getState();              // { label, windowLabel, url, title }
await wv.openDevtools();
await wv.evalScript('window.scrollTo(0, 0)');
const value = await wv.evalWithCallback<number>('1 + 1');

// 运行时身份:所有 webview 共享的浏览器后端元数据
const runtime = await runtimeInfo();
// { name: 'cef', webviewVersion: '150.0.11', resourceMode: 'scheme', origin: 'tarui://localhost/' }

// 在一个既有窗口内挂载额外的 webview(bounds 省略时填满窗口)
const panel = await Webview.create({
  label: 'side',
  windowLabel: 'main',
  url: 'tarui://localhost/side',
  bounds: { x: 640, y: 0, width: 320, height: 480 },
});
await panel.setBounds({ x: 0, y: 0, width: 200, height: 200 }); // 重新定位
await panel.setBounds(null);                                     // 恢复填满窗口
await panel.destroy();                                           // 卸载并释放

// CDP:发送一条 Chrome DevTools Protocol 消息并订阅浏览器回传
await wv.sendDevtoolsMessage('{"id":1,"method":"Runtime.enable"}');
const off = wv.onDevtoolsProtocol(({ message }) => console.log('cdp', message));
```

`Webview.create()` 对应 `plugin:webview|create`(在既有窗口内挂载新 surface,宿主窗口继承其 capability;跨窗口需 `plugin:webview|create-other-window`)。`setBounds(null)` / `destroy()` 分别对应 `plugin:webview|set-bounds`(受 `-other-webview` 约束)与 `plugin:webview|destroy`。`sendDevtoolsMessage()` 对应 `plugin:webview|send-devtools-message`(受权限 + `-other-webview` 约束,默认不授权;承载后端不支持 CDP 时报 `NOT_SUPPORTED`),`onDevtoolsProtocol` 订阅保留事件 `webview://devtools-protocol`(须在 capability `events` 中声明)。列表与生命周期订阅:`Webview.getAll()`、`onDownloadRequested` / `onNavigationRequested` / `onRenderProcessGone`。

## 9. Updater

```ts
import { check, download, apply, restore, verify, onStatus } from '@lytree/api/updater';

const status = await check();          // { updateAvailable, version?, error? }
if (status.updateAvailable) {
  const staged = await download();     // { succeeded, stagingPath?, error? }
  if (staged.succeeded && staged.stagingPath) {
    await verify(cachedManifestJson, staged.stagingPath);
    await apply(staged.stagingPath);   // 安装前再次验签 + 逐文件复算哈希
  }
}

// 从调用方缓存的已签名清单恢复更新(无网络),镜像 Tauri `Updater.restoreUpdate`
const restored = await restore(cachedManifestJson);
```

对应命令 `plugin:updater|check` / `download` / `apply` / `restore` / `verify`;进度与结果通过 `updater://status` 投递(须在 capability `events` 中声明)。`apply` 只接受 `download` 产出的 staging 目录:先重新验签清单,再逐文件复算 SHA-256,安装包仅从清单 `Files` 选取,staging 被替换或加塞都会被 `staged-verification-failed:*` 拒绝。`AllowDowngrades` 是应用级配置(`Tarui:Application:Updater:AllowDowngrades`),开启后版本不同即算更新;默认只接受更高版本。`check` / `restore` 的校验失败返回稳定错误码(`invalid-signature` / `malformed-manifest` / `invalid-version`),不会伪装成"无更新"。
