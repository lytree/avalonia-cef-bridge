import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Window,
  getCurrentWindow,
  emit,
  getAppInfo,
  getOsInfo,
  on,
  openDialog,
  saveDialog,
  openExternal,
  readText,
  resolvePath,
  writeText,
  type AppInfo,
  type OsInfo,
  type Unlisten,
  type WindowState,
} from '@tarui/api'
import './App.css'

type Health = 'ready' | 'checking' | 'offline'

type LogEntry = {
  id: number
  source: string
  detail: string
}

let logSequence = 0

function formatSize(state: WindowState | null): string {
  if (!state) return '—'
  return `${state.size.width}×${state.size.height} @ ${state.position.x},${state.position.y}`
}

function App() {
  const [health, setHealth] = useState<Health>('checking')
  const [appInfo, setAppInfo] = useState<AppInfo | null>(null)
  const [os, setOs] = useState<OsInfo | null>(null)
  const [state, setState] = useState<WindowState | null>(null)
  const [monitorCount, setMonitorCount] = useState<number | null>(null)
  const [windowCount, setWindowCount] = useState<number | null>(null)
  const [lastCommand, setLastCommand] = useState('No native command invoked')
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [closeRequested, setCloseRequested] = useState(false)

  const currentWindow = useMemo(() => getCurrentWindow(), [])

  const addLog = useCallback((source: string, detail: string) => {
    logSequence += 1
    setLogs(previous => [{ id: logSequence, source, detail }, ...previous].slice(0, 10))
  }, [])

  const refreshState = useCallback(() => {
    void currentWindow
      .getState()
      .then(setState)
      .catch(() => setState(null))
    void currentWindow
      .monitors()
      .then(monitors => setMonitorCount(monitors.length))
      .catch(() => setMonitorCount(null))
    void Window.getAll()
      .then(windows => setWindowCount(windows.length))
      .catch(() => setWindowCount(null))
  }, [currentWindow])

  useEffect(() => {
    void getAppInfo()
      .then(info => {
        setAppInfo(info)
        setHealth('ready')
      })
      .catch(() => setHealth('offline'))

    void getOsInfo()
      .then(setOs)
      .catch(() => setOs(null))

    refreshState()
  }, [refreshState])

  useEffect(() => {
    const unlisteners: Unlisten[] = [
      on<{ theme: string }>('shell://theme-changed', payload =>
        addLog('shell', `theme changed → ${payload.theme}`),
      ),
      currentWindow.onFocusChanged(focused =>
        addLog('window', focused ? 'focus gained' : 'focus lost'),
      ),
      currentWindow.onMoved(geometry =>
        addLog('window', `moved → ${geometry.x},${geometry.y}`),
      ),
      currentWindow.onResized(geometry =>
        addLog('window', `resized → ${geometry.width}×${geometry.height}`),
      ),
      currentWindow.onCloseRequested(() => {
        addLog('window', 'close requested — awaiting confirmation')
        setCloseRequested(true)
      }),
      currentWindow.onDestroyed(() => addLog('window', 'window destroyed')),
      on<{ message: string }>('demo://ping', payload =>
        addLog('event', `demo://ping "${payload.message}"`),
      ),
    ]

    return () => {
      for (const unlisten of unlisteners) {
        unlisten()
      }
    }
  }, [currentWindow, addLog])

  useEffect(() => {
    const unlisten = currentWindow.onMoved(refreshState)
    const unlistenResized = currentWindow.onResized(refreshState)
    return () => {
      unlisten()
      unlistenResized()
    }
  }, [currentWindow, refreshState])

  const statusLabel = useMemo(() => {
    if (health === 'checking') return 'Checking shell'
    if (health === 'ready') return 'Native shell connected'
    return 'Browser preview mode'
  }, [health])

  const run = useCallback(
    async (label: string, action: () => Promise<string>): Promise<void> => {
      try {
        const detail = await action()
        setLastCommand(`${label}: ${detail}`)
        addLog('invoke', `${label} ok`)
      } catch (error) {
        const message = error instanceof Error ? error.message : `${label} failed`
        setLastCommand(`${label}: ${message}`)
        addLog('invoke', `${label} failed`)
      }
    },
    [addLog],
  )

  function windowAction(label: string, action: () => Promise<void>, done: string) {
    return () =>
      void run(label, async () => {
        await action()
        refreshState()
        return done
      })
  }

  const actions = {
    openDocument: () =>
      void run('dialog.open', async () => {
        const paths = await openDialog({ extensions: ['json', 'yaml', 'md'] })
        return paths.length > 0 ? paths.join(', ') : 'dialog cancelled'
      }),
    saveDocument: () =>
      void run('dialog.save', async () => {
        const path = await saveDialog({ defaultName: 'tarui-notes.json' })
        return path ?? 'dialog cancelled'
      }),
    clipboard: () =>
      void run('clipboard', async () => {
        const sample = `tarui.net clipboard roundtrip at ${new Date().toISOString()}`
        await writeText(sample)
        const read = await readText()
        return read === sample ? `roundtrip ok (${read.length} chars)` : `mismatch: ${read}`
      }),
    openExternal: () =>
      void run('shell.open', async () => {
        const result = await openExternal('https://github.com')
        return result.opened ? 'opened in default browser' : `failed: ${result.error ?? 'unknown'}`
      }),
    resolveHome: () =>
      void run('path.resolve', async () => {
        const home = await resolvePath('home')
        const appData = await resolvePath('appData')
        return `home=${home} appData=${appData}`
      }),
    emitPing: () =>
      void run('event.emit', async () => {
        await emit('demo://ping', { message: `hello from window ${state?.label ?? 'main'}` })
        return 'demo://ping emitted (broadcast)'
      }),
    createWindow: () =>
      void run('window.create', async () => {
        await Window.create({
          label: `secondary-${Date.now() % 1000}`,
          title: 'tarui.net — secondary',
          width: 720,
          height: 480,
        })
        refreshState()
        return 'secondary window created'
      }),
    confirmClose: () =>
      void run('window.close', async () => {
        await currentWindow.close()
        return 'close confirmed'
      }),
  }

  return (
    <main className="shell-preview">
      <header className="topbar">
        <div className="brand-mark" aria-hidden="true">t/</div>
        <div>
          <p className="eyebrow">tarui.net / workspace</p>
          <h1>Native shell, web business UI.</h1>
        </div>
        <div className={`connection-pill ${health}`}>
          <span className="status-dot" />
          {statusLabel}
        </div>
      </header>

      {closeRequested && (
        <div className="close-bar" role="alert">
          <span>Close requested by the window manager.</span>
          <div className="close-bar-actions">
            <button type="button" onClick={actions.confirmClose}>
              Confirm close
            </button>
            <button
              type="button"
              className="quiet"
              onClick={() => setCloseRequested(false)}
            >
              Stay
            </button>
          </div>
        </div>
      )}

      <section className="hero-grid">
        <div className="intro-panel">
          <p className="eyebrow">Desktop foundation</p>
          <h2>One calm place for native capabilities and fast product work.</h2>
          <p className="lede">
            Avalonia owns the window and system surface. React owns the business page.
            The bridge stays typed, explicit, and small.
          </p>
          <div className="actions">
            <button type="button" onClick={actions.openDocument}>
              <span aria-hidden="true">+</span>
              Open a document
            </button>
            <button type="button" className="quiet" onClick={actions.saveDocument}>
              Save as…
            </button>
            <button
              type="button"
              className="quiet"
              onClick={windowAction('window.minimize', () => currentWindow.minimize(), 'minimized')}
            >
              Minimize shell
            </button>
          </div>
          <p className="command-result">{lastCommand}</p>
        </div>

        <aside className="status-panel" aria-label="Runtime status">
          <div className="panel-heading">
            <span>Runtime surface</span>
            <span className="panel-code">
              {appInfo ? `v${appInfo.shellVersion}` : 'v—'}
            </span>
          </div>
          <div className="metric-row">
            <span>Bridge</span>
            <strong>{health === 'ready' ? 'Connected' : 'Local preview'}</strong>
          </div>
          <div className="metric-row">
            <span>Platform</span>
            <strong>{os ? `${os.platform} / ${os.arch}` : '—'}</strong>
          </div>
          <div className="metric-row">
            <span>Locale</span>
            <strong>{os?.locale ?? '—'}</strong>
          </div>
          <div className="metric-row">
            <span>Window</span>
            <strong>{state ? `${state.label} · ${state.theme}` : '—'}</strong>
          </div>
          <div className="metric-row">
            <span>Geometry</span>
            <strong>{formatSize(state)}</strong>
          </div>
          <div className="metric-row">
            <span>Monitors</span>
            <strong>{monitorCount ?? '—'}</strong>
          </div>
          <div className="metric-row">
            <span>Windows</span>
            <strong>{windowCount ?? '—'}</strong>
          </div>
          <div className="metric-row">
            <span>Permissions</span>
            <strong>{appInfo ? `${appInfo.capabilities.length} granted` : '—'}</strong>
          </div>
          <div className="capability-list">
            {(appInfo?.capabilities ?? ['bridge offline']).slice(0, 6).map(capability => (
              <span key={capability}>{capability}</span>
            ))}
          </div>
        </aside>
      </section>

      <section className="playground" aria-label="Capability playground">
        <div className="section-heading">
          <span className="section-index">01</span>
          <h3>Window controls</h3>
        </div>
        <div className="chip-row">
          <button type="button" className="quiet" onClick={windowAction('window.maximize', () => currentWindow.toggleMaximize(), 'maximize toggled')}>
            Toggle maximize
          </button>
          <button type="button" className="quiet" onClick={windowAction('window.center', () => currentWindow.center(), 'centered')}>
            Center
          </button>
          <button type="button" className="quiet" onClick={windowAction('window.fullscreen', () => currentWindow.setFullscreen(!(state?.isFullscreen ?? false)), 'fullscreen toggled')}>
            Toggle fullscreen
          </button>
          <button type="button" className="quiet" onClick={windowAction('window.always-on-top', () => currentWindow.setAlwaysOnTop(!(state?.isAlwaysOnTop ?? false)), 'always-on-top toggled')}>
            Toggle always-on-top
          </button>
          <button type="button" className="quiet" onClick={windowAction('window.set-title', () => currentWindow.setTitle(`tarui.net — ${new Date().toLocaleTimeString()}`), 'title updated')}>
            Update title
          </button>
          <button type="button" className="quiet" onClick={actions.createWindow}>
            Create window
          </button>
        </div>

        <div className="section-heading">
          <span className="section-index">02</span>
          <h3>System surface</h3>
        </div>
        <div className="chip-row">
          <button type="button" className="quiet" onClick={actions.clipboard}>
            Clipboard roundtrip
          </button>
          <button type="button" className="quiet" onClick={actions.openExternal}>
            Open URL externally
          </button>
          <button type="button" className="quiet" onClick={actions.resolveHome}>
            Resolve paths
          </button>
          <button type="button" className="quiet" onClick={actions.emitPing}>
            Emit demo event
          </button>
        </div>
      </section>

      <section className="event-dock" aria-label="Event stream">
        <div className="panel-heading">
          <span>Event stream</span>
          <span className="panel-code">live</span>
        </div>
        {logs.length === 0 ? (
          <p className="event-empty">Waiting for shell events — move or resize the window.</p>
        ) : (
          <ul className="event-log">
            {logs.map(entry => (
              <li key={entry.id}>
                <span className="event-source">{entry.source}</span>
                <span className="event-detail">{entry.detail}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="principles">
        <div>
          <span className="section-index">a</span>
          <h3>Typed bridge</h3>
          <p>
            Every command and event crosses the boundary as an explicit contract —
            no stringly-typed payloads, no hidden globals.
          </p>
        </div>
        <div>
          <span className="section-index">b</span>
          <h3>Capability scoped</h3>
          <p>
            Windows receive only the permissions declared in their capability file;
            the router rejects everything else at dispatch time.
          </p>
        </div>
        <div>
          <span className="section-index">c</span>
          <h3>Plugin shaped</h3>
          <p>
            Core, window, dialog, event, and system plugins register commands
            through one router — new surfaces stay additive.
          </p>
        </div>
      </section>
    </main>
  )
}

export default App
