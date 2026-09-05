import { useEffect, useRef, useState } from 'react'
import { getAppInfo } from '@lytree/api/app'
import type { AppInfo } from '@lytree/api/app'
import { getCurrentWindow } from '@lytree/api/window'
import type { WindowState } from '@lytree/api/window'
import { Webview } from '@lytree/api/webview'
import { emit, on } from '@lytree/api/event'
import { readHtml, writeHtml } from '@lytree/api/clipboard'
import { ask } from '@lytree/api/dialog'
import { showContextMenu } from '@lytree/api/menu'
import { fs, readFileStream, writeFileChunked } from '@lytree/api/fs'
import { store } from '@lytree/api/store'
import { http } from '@lytree/api/http'
import { shell } from '@lytree/api/shell'
import { checkUpdate, downloadUpdate, applyUpdate } from '@lytree/api/updater'
import { cookie as cookieApi } from '@lytree/api/cookie'
import { platform } from '@lytree/api/platform'
import type { PlatformCapabilities } from '@lytree/api/platform'
import { Channel, invoke } from '@lytree/api/ipc'

type StreamProgress = { step: number; total: number }

type LogEntry = { at: string; text: string; kind: 'info' | 'ok' | 'err' }

function now(): string {
  return new Date().toLocaleTimeString()
}

/** Applies the WinUI 3 theme to the page: data-theme drives the CSS tokens. */
function applyTheme(theme: string) {
  const resolved = theme === 'dark' ? 'dark' : 'light'
  document.documentElement.dataset.theme = resolved
  document.documentElement.style.colorScheme = resolved
  // 与原生标题栏背景保持连续（WinUI 3 暗色基底 #202020）
  document.body.style.background = resolved === 'dark' ? '#202020' : ''
}

function App() {
  const [info, setInfo] = useState<AppInfo | null>(null)
  const [winState, setWinState] = useState<WindowState | null>(null)
  const [storeKey, setStoreKey] = useState('greeting')
  const [storeValue, setStoreValue] = useState('Hello from Tarui!')
  const [storeKeys, setStoreKeys] = useState<string[]>([])
  const [filePath, setFilePath] = useState('demo/hello.txt')
  const [fileContents, setFileContents] = useState('Hello Tarui.')
  const [dirEntries, setDirEntries] = useState<string[]>([])
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [unknownUi, setUnknownUi] = useState('')
  const [nativeClicks, setNativeClicks] = useState(0)
  const [streamFrames, setStreamFrames] = useState<StreamProgress[]>([])
  const [streaming, setStreaming] = useState(false)
  const [bigFileStatus, setBigFileStatus] = useState('')
  const [httpReads, setHttpReads] = useState(0)
  const [lastHttp, setLastHttp] = useState('')
  const [lastUpload, setLastUpload] = useState('')
  const [shellLines, setShellLines] = useState<string[]>([])
  const [shellRunning, setShellRunning] = useState(false)
  const [lastAsk, setLastAsk] = useState<boolean | null>(null)
  const [updaterStatus, setUpdaterStatus] = useState('')
  const [platformCaps, setPlatformCaps] = useState<PlatformCapabilities | null>(null)
  const [watchId, setWatchId] = useState<string | null>(null)
  const [cookieUrl, setCookieUrl] = useState('https://example.com')
  const [cookieLines, setCookieLines] = useState<string[]>([])

  const logRef = useRef<LogEntry[]>([])
  const pushLog = (entry: Omit<LogEntry, 'at' | 'kind'> & { kind?: LogEntry['kind'] }) => {
    const item: LogEntry = { at: now(), kind: entry.kind ?? 'info', text: entry.text }
    const next = [...logRef.current, item]
    logRef.current = next
    setLogs(next)
  }

  // Mirror any event routed to this webview so `event:emit` demos are observable.
  useEffect(() => {
    return on<{ source: string }>('user://demo/echo', payload => {
      pushLog({ text: `user://demo/echo received: ${JSON.stringify(payload)}` })
    })
  }, [])

  // Context-menu and window-menu item clicks arrive here.
  useEffect(() => {
    return on<{ id: string; text?: string | null }>('menu://item-clicked', payload => {
      pushLog({ kind: 'ok', text: `menu://item-clicked -> ${payload.id}${payload.text ? ` (${payload.text})` : ''}` })
    })
  }, [])

  // Surface which OS-coupled features are genuinely available on this platform.
  useEffect(() => {
    platform.capabilities().then(setPlatformCaps).catch(() => setPlatformCaps(null))
  }, [])

  // Log any fs watch change delivered while a watch is active.
  useEffect(() => {
    return fs.onWatchEvent(event => {
      pushLog({ kind: 'ok', text: `fs watch[${event.watchId}] -> ${event.eventKind} ${event.outputPaths.join(', ')}` })
    })
  }, [])

  // The native sidebar (demo window extension) emits this event when its button is clicked.
  useEffect(() => {
    return on<{ clicks: number }>('user://demo/native-sidebar', payload => {
      setNativeClicks(payload.clicks)
      pushLog({ kind: 'ok', text: `user://demo/native-sidebar received: ${payload.clicks} clicks` })
    })
  }, [])

  // The shell broadcasts shell://theme-changed whenever the app theme variant changes
  // (the title-bar theme toggle sets RequestedThemeVariant), so the page follows along.
  useEffect(() => {
    return on<{ theme: string }>('shell://theme-changed', payload => {
      applyTheme(payload.theme)
      pushLog({ kind: 'ok', text: `shell://theme-changed -> ${payload.theme === 'dark' ? 'dark' : 'light'}` })
    })
  }, [])

  useEffect(() => {
    const window = getCurrentWindow()
    async function boot() {
      try {
        setInfo(await getAppInfo())
        const state = await window.getState()
        setWinState(state)
        // 启动即同步当前主题：shell 的 theme-changed 首播可能早于页面加载，页面必须主动拉取。
        applyTheme(state.theme)
        setStoreKeys((await store.keys()).keys)
      } catch (err) {
        pushLog({ kind: 'err', text: `boot: ${String(err)}` })
      }
    }
    void boot()

    // Window event subscriptions (raw IPC listen round-trips back to the host).
    const unsubs = [
      window.onMoved(geo => pushLog({ text: `moved -> ${geo.x},${geo.y}` })),
      window.onResized(geo => pushLog({ text: `resized -> ${geo.width}x${geo.height}` })),
      window.onFocusChanged(focused => pushLog({ text: `focus ${focused ? 'in' : 'out'}` })),
      // A close click is intercepted by the shell; it emits close-requested and
      // waits for the frontend to confirm by invoking core:window|close.
      window.onCloseRequested(() => {
        void window.close()
      }),
    ]
    return () => unsubs.forEach(fn => fn())
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function refreshWindow() {
    try {
      setWinState(await getCurrentWindow().getState())
      pushLog({ kind: 'ok', text: 'window state refreshed' })
    } catch (err) {
      pushLog({ kind: 'err', text: `get-state: ${String(err)}` })
    }
  }

  async function setTitle() {
    try {
      await getCurrentWindow().setTitle('Tarui Demo — custom title')
      pushLog({ kind: 'ok', text: 'title updated' })
    } catch (err) {
      pushLog({ kind: 'err', text: `set-title: ${String(err)}` })
    }
  }

  async function toggleFullscreen() {
    try {
      const s = winState
      await getCurrentWindow().setFullscreen(s ? !s.isFullscreen : true)
      await refreshWindow()
    } catch (err) {
      pushLog({ kind: 'err', text: `set-fullscreen: ${String(err)}` })
    }
  }

  async function doStoreSet() {
    try {
      await store.set({ key: storeKey, value: storeValue })
      setStoreKeys((await store.keys()).keys)
      pushLog({ kind: 'ok', text: `store.set(${storeKey}) -> ${storeValue}` })
    } catch (err) {
      pushLog({ kind: 'err', text: `store.set: ${String(err)}` })
    }
  }

  async function doStoreGet() {
    try {
      const { value } = await store.get({ key: storeKey })
      setStoreValue(value ?? '')
      pushLog({ kind: 'ok', text: `store.get(${storeKey}) -> ${String(value)}` })
    } catch (err) {
      pushLog({ kind: 'err', text: `store.get: ${String(err)}` })
    }
  }

  async function doStoreRemove() {
    try {
      await store.remove({ key: storeKey })
      setStoreKeys((await store.keys()).keys)
      pushLog({ kind: 'ok', text: `store.remove(${storeKey})` })
    } catch (err) {
      pushLog({ kind: 'err', text: `store.remove: ${String(err)}` })
    }
  }

  async function doFileWrite() {
    try {
      await fs.writeTextFile({ base: 'appData', path: filePath, contents: fileContents })
      pushLog({ kind: 'ok', text: `fs.write appData/${filePath}` })
    } catch (err) {
      pushLog({ kind: 'err', text: `fs.write: ${String(err)}` })
    }
  }

  async function doFileRead() {
    try {
      const { contents } = await fs.readTextFile({ base: 'appData', path: filePath })
      setFileContents(contents)
      pushLog({ kind: 'ok', text: `fs.read appData/${filePath} -> ${contents}` })
    } catch (err) {
      pushLog({ kind: 'err', text: `fs.read: ${String(err)}` })
    }
  }

  async function doToggleWatch() {
    if (watchId) {
      try {
        await fs.unwatch(watchId)
        setWatchId(null)
        pushLog({ kind: 'ok', text: `fs.unwatch(${watchId})` })
      } catch (err) {
        pushLog({ kind: 'err', text: `fs.unwatch: ${String(err)}` })
      }
      return
    }
    try {
      const result = await fs.watch({ base: 'temp' })
      setWatchId(result.watchId)
      pushLog({ kind: 'ok', text: `fs.watch(temp) -> ${result.watchId}` })
    } catch (err) {
      pushLog({ kind: 'err', text: `fs.watch: ${String(err)}` })
    }
  }

  function showCookies(lines: string[]) {
    setCookieLines(lines)
    pushLog({ kind: 'ok', text: `cookie: listed ${lines.length} cookies for ${cookieUrl}` })
  }

  async function doCookieList() {
    try {
      const result = await cookieApi.list(cookieUrl)
      if (!result.supported) {
        setCookieLines([`平台不支持 Cookie 存储：${result.error ?? 'n/a'}`])
        pushLog({ kind: 'err', text: 'cookie.list: unsupported' })
        return
      }
      showCookies(result.cookies.map(c => `${c.name}=${c.value}${c.httpOnly ? ' [HttpOnly]' : ''}`))
    } catch (err) {
      pushLog({ kind: 'err', text: `cookie.list: ${String(err)}` })
    }
  }

  async function doCookieSet() {
    try {
      const result = await cookieApi.set(cookieUrl, {
        name: 'demo_theme',
        value: 'dark',
        path: '/',
        secure: cookieUrl.startsWith('https://'),
      })
      pushLog({
        kind: result.succeeded ? 'ok' : 'err',
        text: result.succeeded ? 'cookie.set(demo_theme) -> ok' : `cookie.set 失败：${result.error ?? 'unknown'}`,
      })
    } catch (err) {
      pushLog({ kind: 'err', text: `cookie.set: ${String(err)}` })
    }
  }

  async function doCookieRemove() {
    try {
      const result = await cookieApi.remove(cookieUrl, 'demo_theme')
      pushLog({
        kind: result.succeeded ? 'ok' : 'err',
        text: result.succeeded ? 'cookie.remove(demo_theme) -> ok' : `cookie.remove 失败：${result.error ?? 'unknown'}`,
      })
    } catch (err) {
      pushLog({ kind: 'err', text: `cookie.remove: ${String(err)}` })
    }
  }

  async function doListDir() {
    try {
      const entries = await fs.readDir({ base: 'appData' })
      setDirEntries(entries.map(e => `${e.isDirectory ? '[d]' : '[f]'} ${e.name}`))
      pushLog({ kind: 'ok', text: `fs.readDir appData -> ${entries.length} entries` })
    } catch (err) {
      pushLog({ kind: 'err', text: `fs.readDir: ${String(err)}` })
    }
  }

  async function doEmit() {
    try {
      await emit('user://demo/echo', { source: 'webview' })
      pushLog({ kind: 'ok', text: 'event emitted user://demo/echo (echo visible in log)' })
    } catch (err) {
      pushLog({ kind: 'err', text: `emit: ${String(err)}` })
    }
  }

  async function doStreamEcho() {
    const channel = new Channel<StreamProgress>()
    const frames: StreamProgress[] = []
    channel.onmessage = frame => {
      frames.push(frame)
      setStreamFrames([...frames])
    }
    try {
      setStreaming(true)
      await invoke('core:channel|stream-echo', { channel, count: 10 })
      pushLog({ kind: 'ok', text: `core:channel|stream-echo completed (${frames.length} frames)` })
    } catch (err) {
      pushLog({ kind: 'err', text: `stream-echo: ${String(err)}` })
    } finally {
      setStreaming(false)
    }
  }

  async function doChunkedWrite() {
    // 写入 ~5 MiB 临时文件，验证分片写（256 KiB/块。
    const payload = new Uint8Array(5 * 1024 * 1024)
    for (let index = 0; index < payload.length; index++) {
      payload[index] = (index * 31 + 7) % 251
    }
    try {
      await writeFileChunked('temp', 'stream-demo.bin', payload)
      setBigFileStatus(`写 5 MiB 完成（${Math.ceil(payload.length / (256 * 1024))} 块）`)
      pushLog({ kind: 'ok', text: `writeFileChunked temp/stream-demo.bin -> ${payload.length} bytes` })
    } catch (err) {
      pushLog({ kind: 'err', text: `writeFileChunked: ${String(err)}` })
      setBigFileStatus('写失败')
    }
  }

  async function doStreamRead() {
    setBigFileStatus('')
    let frames = 0
    let total = 0
    try {
      const result = await readFileStream({
        base: 'temp',
        path: 'stream-demo.bin',
        chunkBytes: 512 * 1024,
        onEvent: event => {
          frames += 1
          if (event.kind === 'chunk') {
            total += event.data.byteLength
          }
          setBigFileStatus(`读取中… ${frames} 帧 / ${total} 字节`)
        },
      })
      setBigFileStatus(`读取完成：${result.size} 字节，${frames} 帧`)
      pushLog({ kind: 'ok', text: `readFileStream temp/stream-demo.bin -> ${result.size} bytes over ${frames} frames` })
    } catch (err) {
      pushLog({ kind: 'err', text: `readFileStream: ${String(err)}` })
      setBigFileStatus('读取失败')
    }
  }

  async function doHttpFetch() {
    try {
      const res = await http.fetch('https://api.github.com/zen')
      setHttpReads(count => count + 1)
      setLastHttp(`${res.status} ${res.ok ? 'OK' : 'ERR'}: ${(res.body ?? '').trim().slice(0, 120)}`)
      pushLog({ kind: 'ok', text: `http.fetch(zen) -> ${res.status}` })
    } catch (err) {
      pushLog({ kind: 'err', text: `http.fetch: ${String(err)}` })
      setLastHttp(`失败: ${String(err)}`)
    }
  }

  async function doUpload() {
    // 前端仅演示受作用域约束的 multipart 上传路径；本地无回显服务，通常以连接失败收尾并如实记录。
    try {
      const res = await http.upload('http://127.0.0.1:9/upload', {
        fields: [{ name: 'note', value: 'hello-tarui' }],
        files: [{ name: 'file', fileName: 'readme.txt', data: new TextEncoder().encode('file-contents'), contentType: 'text/plain' }],
      })
      setLastUpload(`上传 ${res.status}`)
      pushLog({ kind: 'ok', text: `http.upload -> ${res.status}` })
    } catch (err) {
      setLastUpload(`失败: ${String(err)}`)
      pushLog({ kind: 'err', text: `http.upload: ${String(err)}` })
    }
  }

  async function doShellRun() {
    setShellRunning(true)
    setShellLines([])
    let text = ''
    try {
      const result = await shell.spawn('ping.exe', {
        args: ['-n', '3', '127.0.0.1'],
        onEvent: event => {
          if (event.kind === 'stdout') {
            text += new TextDecoder().decode(event.data)
            setShellLines(text.split('\n').map(line => line.trimEnd()).filter(Boolean))
          } else if (event.kind === 'terminated') {
            pushLog({ kind: 'ok', text: `shell spawn terminated with code ${event.code}` })
          }
        },
      })
      pushLog({ kind: 'ok', text: `shell.spawn(ping.exe) -> ${result.id}` })
    } catch (err) {
      pushLog({ kind: 'err', text: `shell.spawn: ${String(err)}` })
    } finally {
      setShellRunning(false)
    }
  }

  async function doAsk() {
    try {
      const answer = await ask({
        title: 'Keep changes?',
        content: 'Unsaved edits will be lost.',
        cancelLabel: 'Cancel',
      })
      setLastAsk(answer)
      pushLog({ kind: 'ok', text: `ask -> ${answer === null ? 'cancel' : answer ? 'Yes' : 'No'}` })
    } catch (err) {
      pushLog({ kind: 'err', text: `dialog.ask: ${String(err)}` })
    }
  }

  async function doContextMenu() {
    try {
      await showContextMenu({
        items: [
          { id: 'ctx-copy', text: 'Copy' },
          { id: 'ctx-paste', text: 'Paste', enabled: false },
          { id: 'ctx-divider', kind: 'divider' },
          {
            id: 'ctx-share',
            text: 'Share',
            items: [
              { id: 'ctx-email', text: 'Email' },
              { id: 'ctx-link', text: 'Copy link' },
            ],
          },
          { id: 'ctx-bold', kind: 'check', text: 'Bold', checked: true },
        ],
        x: 140,
        y: 40,
      })
      pushLog({ kind: 'ok', text: 'context menu popped at (140, 40)' })
    } catch (err) {
      pushLog({ kind: 'err', text: `showContextMenu: ${String(err)}` })
    }
  }

  async function doUpdateFlow() {
    setUpdaterStatus('checking...')
    pushLog({ kind: 'info', text: 'updater: check' })
    try {
      const check = await checkUpdate()
      if (check.error) {
        setUpdaterStatus(`check 失败: ${check.error}`)
        pushLog({ kind: 'err', text: `updater.check: ${check.error}` })
        return
      }
      if (!check.updateAvailable || !check.version) {
        setUpdaterStatus('无可用更新')
        pushLog({ kind: 'ok', text: 'updater: no update available' })
        return
      }

      pushLog({ kind: 'info', text: `updater: downloading v${check.version}` })
      const download = await downloadUpdate()
      if (!download.succeeded || !download.stagingPath) {
        setUpdaterStatus(`download 失败: ${download.error ?? 'unknown'}`)
        pushLog({ kind: 'err', text: `updater.download: ${download.error ?? 'unknown'}` })
        return
      }

      pushLog({ kind: 'info', text: 'updater: applying staged bundle' })
      const apply = await applyUpdate(download.stagingPath, true)
      setUpdaterStatus(apply.succeeded ? `已应用（重启=${apply.restart}）` : `apply 失败: ${apply.error}`)
      pushLog({ kind: apply.succeeded ? 'ok' : 'err', text: `updater.apply -> ${apply.succeeded ? 'ok' : apply.error ?? 'failed'}` })
    } catch (err) {
      setUpdaterStatus(`异常: ${String(err)}`)
      pushLog({ kind: 'err', text: `updater: ${String(err)}` })
    }
  }

  return (
    <main className="app">
      <h1>Tarui Demo</h1>
      {info && (
        <p className="muted">
          {info.product} v{info.shellVersion} · bridge {info.bridgeVersion} · {info.platform}
          {' · '}
          {info.capabilities.length} capabilities
        </p>
      )}

      <section>
        <h2>窗口 + IPC</h2>
        {winState && (
          <div className="grid">
            <span>标题</span><code>{winState.title}</code>
            <span>尺寸</span><code>{winState.size.width}×{winState.size.height}</code>
            <span>位置</span><code>{winState.position.x},{winState.position.y}</code>
            <span>状态</span>
            <code>
              {winState.isFocused ? '聚焦' : '失焦'} · {winState.isFullscreen ? '全屏' : '窗口'} ·{' '}
              {winState.isMaximized ? '最大化' : '还原'} · {winState.isMinimized ? '最小化' : '运行'}
            </code>
          </div>
        )}
        <div className="row">
          <button onClick={refreshWindow}>刷新状态</button>
          <button onClick={setTitle}>修改标题</button>
          <button onClick={toggleFullscreen}>切换全屏</button>
          <button onClick={() => getCurrentWindow().toggleMaximize().then(refreshWindow)}>
            最大化切换
          </button>
        </div>
        <p className="muted">拖动或缩放窗口，事件日志会实时记录 moved / resized / focus 事件。</p>
      </section>

      <section>
        <h2>Store 存储</h2>
        <div className="row">
          <input value={storeKey} onChange={e => setStoreKey(e.target.value)} placeholder="key" />
          <input value={storeValue} onChange={e => setStoreValue(e.target.value)} placeholder="value" />
          <button onClick={doStoreSet}>set</button>
          <button onClick={doStoreGet}>get</button>
          <button onClick={doStoreRemove}>remove</button>
        </div>
        <p className="muted">当前键：{storeKeys.length ? storeKeys.join(', ') : '（空 store appData/settings.json）'}</p>
      </section>

      <section>
        <h2>剪贴板（HTML / 图片）</h2>
        <div className="row">
          <button
            onClick={() =>
              writeHtml('<b>Tarui 富文本</b>', 'Tarui 纯文本')
                .then(() => pushLog({ kind: 'ok', text: 'clipboard.writeHtml() -> ok' }))
                .catch(err => pushLog({ kind: 'err', text: `clipboard.writeHtml: ${String(err)}` }))
            }
          >
            写 HTML + 文本
          </button>
          <button
            onClick={() =>
              readHtml()
                .then(result => pushLog({
                  kind: result.available ? 'ok' : 'info',
                  text: result.available ? `clipboard.readHtml() -> ${result.html}` : 'clipboard.readHtml(): 剪贴板无 HTML',
                }))
                .catch(err => pushLog({ kind: 'err', text: `clipboard.readHtml: ${String(err)}` }))
            }
          >
            读 HTML
          </button>
        </div>
        <p className="muted">
          写入时同时携带纯文本回退；读取在无 HTML 时诚实报告不可用，而非伪造空串。图片读写以 PNG 字节承载。
        </p>
      </section>

      <section>
        <h2>FS 文件系统（隔离 appData 目录）</h2>
        <div className="row">
          <input value={filePath} onChange={e => setFilePath(e.target.value)} placeholder="相对路径" />
          <button onClick={doFileWrite}>写入</button>
          <button onClick={doFileRead}>读取</button>
          <button onClick={doListDir}>列出目录</button>
          <button onClick={doToggleWatch}>{watchId ? '停止监视 temp' : '监视 temp 目录'}</button>
        </div>
        <textarea
          rows={3}
          value={fileContents}
          onChange={e => setFileContents(e.target.value)}
          placeholder="文件内容"
        />
        {dirEntries.length > 0 && (
          <ul className="muted">
            {dirEntries.map(e => (
              <li key={e}>{e}</li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <h2>大文件流式 + HTTP</h2>
        <div className="row">
          <button onClick={doChunkedWrite}>分片写 5 MiB</button>
          <button onClick={doStreamRead}>流式读回</button>
          <button onClick={doHttpFetch}>HTTP 探测 ({httpReads})</button>
          <button onClick={doUpload}>multipart 上传</button>
        </div>
        {bigFileStatus && <p className="muted">{bigFileStatus}</p>}
        {lastHttp && <p className="muted">http.fetch: {lastHttp}</p>}
        {lastUpload && <p className="muted">http.upload: {lastUpload}</p>}
        <p className="muted">
          分片写走 native 临时文件原子提交；流式读逐帧回调呈现进度；HTTP 探测受能力作用域（github API）约束。
        </p>
      </section>

      <section>
        <h2>Shell 子进程（作用域白名单）</h2>
        <div className="row">
          <button onClick={doShellRun} disabled={shellRunning}>
            {shellRunning ? '运行中…' : 'spawn ping.exe -n 3'}
          </button>
        </div>
        {shellLines.length > 0 && (
          <pre className="muted">
            {shellLines.map((line, index) => (
              <div key={index}>{line}</div>
            ))}
          </pre>
        )}
        <p className="muted">
          仅允许能力作用域内显式白名单的程序；stdout/stderr 经 Channel 逐行流式回传，退出后回调 terminated。
        </p>
      </section>

      <section>
        <h2>Cookie</h2>
        <div className="row">
          <input value={cookieUrl} onChange={e => setCookieUrl(e.target.value)} placeholder="URL" />
          <button onClick={doCookieList}>列出</button>
          <button onClick={doCookieSet}>设置 demo_theme=dark</button>
          <button onClick={doCookieRemove}>删除 demo_theme</button>
        </div>
        {cookieLines.length > 0 && (
          <pre className="muted">
            {cookieLines.map((line, index) => (
              <div key={index}>{line}</div>
            ))}
          </pre>
        )}
        <p className="muted">
          经浏览器全局 Cookie 存储（CEF）。宿主无 Cookie 存储时诚实降级并报告原因，而不是伪造空列表。
        </p>
      </section>

      <section>
        <h2>WebView DevTools</h2>
        <div className="row">
          <button
            onClick={() =>
              Webview.getCurrent()
                .openDevtools()
                .then(() => pushLog({ kind: 'ok', text: 'webview.openDevtools() -> ok' }))
                .catch(err => pushLog({ kind: 'err', text: `webview.openDevtools: ${String(err)}` }))
            }
          >
            打开 DevTools
          </button>
          <button
            onClick={() =>
              Webview.getCurrent()
                .closeDevtools()
                .then(() => pushLog({ kind: 'ok', text: 'webview.closeDevtools() -> ok' }))
                .catch(err => pushLog({ kind: 'err', text: `webview.closeDevtools: ${String(err)}` }))
            }
          >
            关闭 DevTools
          </button>
        </div>
        <p className="muted">
          经 <code>plugin:webview|devtools</code> 门控；打开附着到浏览器独立的 DevTools 原生窗口，无需配置即隔离于应用页面。
        </p>
      </section>

      <section>
        <h2>Dialog + 上下文菜单</h2>
        <div className="row">
          <button onClick={doAsk}>ask（Yes/No/Cancel）</button>
          <button onClick={doContextMenu}>弹出上下文菜单 (140, 40)</button>
        </div>
        {lastAsk !== null && <p className="muted">上次 ask 结果：{lastAsk === null ? 'cancel' : lastAsk ? 'Yes' : 'No'}</p>}
        <p className="muted">
          ask 是三态询问（Yes/No/关闭即取消）；上下文菜单项点击经 <code>menu://item-clicked</code> 路由到本页日志。
        </p>
      </section>

      <section>
        <h2>Updater 更新流（check → download → apply）</h2>
        <div className="row">
          <button onClick={doUpdateFlow}>运行更新流</button>
        </div>
        {updaterStatus && <p className="muted">updater: {updaterStatus}</p>}
        <p className="muted">
          演示串联校验、暂存与 apply 编排；真实发布需更新服务器 + 签名 + 已签名的 MSIX，apply 成功后会自动重启。
        </p>
      </section>

      <section>
        <h2>平台可用能力（core:platform|capabilities）</h2>
        {platformCaps ? (
          <div className="grid">
            <span>通知</span>
            <code>{platformCaps.notificationSupported ? '支持' : `不支持（${platformCaps.notificationReason ?? 'n/a'}）`}</code>
            <span>全局快捷键</span>
            <code>{platformCaps.globalShortcutSupported ? '支持' : `不支持（${platformCaps.globalShortcutReason ?? 'n/a'}）`}</code>
            <span>开机自启</span>
            <code>{platformCaps.autostartSupported ? '支持' : '不支持'}</code>
            <span>深链</span>
            <code>{platformCaps.deepLinkSupported ? '支持' : `不支持（${platformCaps.deepLinkReason ?? 'n/a'}）`}</code>
          </div>
        ) : (
          <p className="muted">加载平台能力中…</p>
        )}
        <p className="muted">
          前端据此禁用当前平台不可用的 UI，而不是触发运行时诚实降级 no-op。
        </p>
      </section>

      <section>
        <h2>事件</h2>
        <div className="row">
          <input value={unknownUi} onChange={e => setUnknownUi(e.target.value)} placeholder="事件文本（可选）" />
          <button onClick={doEmit}>emit user://demo/echo</button>
        </div>
      </section>

      <section>
        <h2>Channel 流式 IPC</h2>
        <div className="row">
          <button onClick={doStreamEcho} disabled={streaming}>
            {streaming ? '流式传输中…' : '运行 core:channel|stream-echo'}
          </button>
        </div>
        {streamFrames.length > 0 ? (
          <p className="muted">
            已收到 {streamFrames.length} 帧：{streamFrames.map(f => `${f.step}/${f.total}`).join(', ')}
          </p>
        ) : (
          <p className="muted">点击按钮后，原生 handler 会经 Channel 逐帧推回进度。</p>
        )}
      </section>

      <section>
        <h2>原生扩展（Native Sidebar）</h2>
        <p className="muted">
          右侧原生侧边栏由 <code>IShellWindowExtension</code> 注入到 dock 区域。点击其中的
          「Emit to webview」按钮，原生控件会通过 <code>EmitAsync</code> 向此 Web 页面发事件。
        </p>
        <p>
          本页已收到原生点击 <strong>{nativeClicks}</strong> 次。
        </p>
      </section>

      <section>
        <h2>事件日志</h2>
        <pre className="log">
          {logs.length === 0
            ? '（尚无事件，运行上面的操作后此处会显示 IPC 往返结果）'
            : logs.map((l, i) => (
                <div key={i} className={`log-${l.kind}`}>
                  [{l.at}] {l.text}
                </div>
              ))}
        </pre>
      </section>
    </main>
  )
}

export default App