import { useEffect, useMemo, useState } from 'react'
import { invoke, listen } from '@tarui/api'
import './App.css'

type Health = 'ready' | 'checking' | 'offline'

function App() {
  const [health, setHealth] = useState<Health>('checking')
  const [lastCommand, setLastCommand] = useState('No native command invoked')
  const [theme, setTheme] = useState('system')

  useEffect(() => {
    const unlisten = listen<{ theme: string }>('shell://theme-changed', event => {
      setTheme(event.payload.theme)
    })

    void invoke<{ product: string }>('core:app|get-info')
      .then(() => setHealth('ready'))
      .catch(() => setHealth('offline'))

    return () => unlisten()
  }, [])

  const statusLabel = useMemo(() => {
    if (health === 'checking') return 'Checking shell'
    if (health === 'ready') return 'Native shell connected'
    return 'Browser preview mode'
  }, [health])

  async function openFile() {
    try {
      const result = await invoke<{ paths: string[] }>('plugin:dialog|open', {
        multiple: false,
        extensions: ['json', 'yaml'],
      })
      setLastCommand(result.paths.length > 0 ? result.paths.join(', ') : 'Dialog cancelled')
    } catch (error) {
      setLastCommand(error instanceof Error ? error.message : 'Dialog unavailable')
    }
  }

  async function minimize() {
    try {
      await invoke('core:window|minimize')
      setLastCommand('Window minimize requested')
    } catch (error) {
      setLastCommand(error instanceof Error ? error.message : 'Window command unavailable')
    }
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

      <section className="hero-grid">
        <div className="intro-panel">
          <p className="eyebrow">Desktop foundation</p>
          <h2>One calm place for native capabilities and fast product work.</h2>
          <p className="lede">
            Avalonia owns the window and system surface. React owns the business page.
            The bridge stays typed, explicit, and small.
          </p>
          <div className="actions">
            <button type="button" onClick={() => void openFile()}>
              <span aria-hidden="true">+</span>
              Open a document
            </button>
            <button type="button" className="quiet" onClick={() => void minimize()}>
              Minimize shell
            </button>
          </div>
          <p className="command-result">{lastCommand}</p>
        </div>

        <aside className="status-panel" aria-label="Runtime status">
          <div className="panel-heading"><span>Runtime surface</span><span className="panel-code">v1</span></div>
          <div className="metric-row"><span>Bridge</span><strong>{health === 'ready' ? 'Connected' : 'Local preview'}</strong></div>
          <div className="metric-row"><span>Theme</span><strong>{theme}</strong></div>
          <div className="metric-row"><span>Permissions</span><strong>4 granted</strong></div>
          <div className="capability-list">
            <span>core:window|minimize</span>
            <span>core:window|close</span>
            <span>plugin:dialog|open</span>
          </div>
        </aside>
      </section>

      <section className="principles">
        <div><span className="section-index">01</span><h3>Thin shell</h3><p>Native controls stay close to the OS and keep business state out of the window tree.</p></div>
        <div><span className="section-index">02</span><h3>Explicit plugins</h3><p>Every plugin is a project reference and a visible Register call in the composition root.</p></div>
        <div><span className="section-index">03</span><h3>Generated contracts</h3><p>Commands and JSON metadata are generated at build time, with no runtime discovery.</p></div>
      </section>
    </main>
  )
}

export default App
