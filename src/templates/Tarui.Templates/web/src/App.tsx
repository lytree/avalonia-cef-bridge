function App() {
  return (
    <main className="app">
      <h1>Tarui App</h1>
      <p>
        Your desktop application is running. When loaded inside the Tarui WebView,
        the host injects a bridge you can reach through{' '}
        <code>window.__tarui__</code>.
      </p>
      <p>
        Run <code>tarui dev</code> for hot reload or <code>tarui build</code> to
        produce a distributable bundle.
      </p>
    </main>
  )
}

export default App