/// <reference types="vite/client" />

// The host injects the IPC bridge on the window object when the page is loaded
// inside the Tarui WebView (see App.tsx). Declared here for editor support.
interface Window {
  __tarui__?: unknown
}