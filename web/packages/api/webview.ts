import { Channel, invoke, listen } from './ipc'
import type { Unlisten } from './ipc'
import { DOWNLOAD_REQUESTED_EVENT, NAVIGATION_REQUESTED_EVENT } from './window'
import type { DownloadRequestEvent, NavigationRequestEvent } from './window'

/** The observable state of a webview and its host window. */
export type WebviewState = {
  label: string
  windowLabel: string
  url: string | null
  title: string
}

/** Fired when the webview renderer process terminates abnormally. */
export const RENDER_PROCESS_GONE_EVENT = 'webview://render-process-gone'

export type RenderProcessGoneEvent = {
  status: 'crashed' | 'killed' | 'other'
  errorCode: number
  error: string | null
}

/** One completion frame streamed back by {@link Webview.evalWithCallback}. */
type EvalFrame = {
  ok: boolean
  value: string | null
  error: string | null
}

/**
 * Typed handle for a shell webview. A webview and its host window share
 * one label while a window hosts a single surface, so a handle addresses
 * the surface independent of the window channel. Instances with no label
 * target the webview that hosts the calling renderer; the shell resolves
 * the label from the request context.
 */
export class Webview {
  readonly label: string | undefined

  private constructor(label: string | undefined) {
    this.label = label
  }

  /** Handle for the webview hosting the calling renderer. */
  static getCurrent(): Webview {
    return new Webview(undefined)
  }

  /** Handle for a webview with a known label. */
  static getByLabel(label: string): Webview {
    return new Webview(label)
  }

  /** Lists the labels of all live webviews and returns handles for them. */
  static async getAll(): Promise<Webview[]> {
    const result = await invoke<{ labels: string[] }>('plugin:webview|list', {})
    return result.labels.map(label => Webview.getByLabel(label))
  }

  /** Navigates this webview to a same-origin URL. */
  async navigate(url: string): Promise<void> {
    await invoke('plugin:webview|navigate', { url, ...this.target() })
  }

  /** Returns the observable state of this webview and its host window. */
  async getState(): Promise<WebviewState> {
    return invoke<WebviewState>('plugin:webview|get-state', this.target())
  }

  /** Opens the browser developer tools for this webview. */
  async openDevtools(): Promise<void> {
    await invoke('plugin:webview|devtools', { open: true, ...this.target() })
  }

  /** Closes the browser developer tools for this webview if they are open. */
  async closeDevtools(): Promise<void> {
    await invoke('plugin:webview|devtools', { open: false, ...this.target() })
  }

  /** Sets the page zoom factor for this webview (1.0 = 100%). */
  async setZoom(factor: number): Promise<void> {
    await invoke('plugin:webview|set-zoom', { factor, ...this.target() })
  }

  /** Opens the browser print dialog for this webview. */
  async print(): Promise<void> {
    await invoke('plugin:webview|print', this.target())
  }

  /** Evaluates a script in this webview. Execution is asynchronous; the result is not observed. */
  async evalScript(script: string): Promise<void> {
    await invoke('plugin:webview|eval', { script, ...this.target() })
  }

  /**
   * Evaluates a script in this webview and resolves with its completion value.
   * The value is the last expression's result with returned promises settled
   * before delivery; non-JSON values (including `undefined`) resolve as null.
   */
  async evalWithCallback<T = unknown>(script: string): Promise<T> {
    const channel = new Channel<EvalFrame>()
    const frame = await new Promise<EvalFrame>(resolve => {
      channel.onmessage = resolve
      invoke('plugin:webview|eval-with-callback', { script, onEvent: channel, ...this.target() }).then(
        () => undefined,
        error => resolve({ ok: false, value: null, error: (error as Error).message }),
      )
    })
    if (!frame.ok) {
      throw new Error(frame.error ?? 'Script evaluation failed')
    }
    return (frame.value === null ? undefined : JSON.parse(frame.value)) as T
  }

  /** Fires for download requests allowed by the host policy and authorized for this webview. */
  onDownloadRequested(handler: (request: DownloadRequestEvent) => void): Unlisten {
    return listen<DownloadRequestEvent>(DOWNLOAD_REQUESTED_EVENT, event => handler(event.payload))
  }

  /** Fires for navigations allowed by the host policy and authorized for this webview. */
  onNavigationRequested(handler: (request: NavigationRequestEvent) => void): Unlisten {
    return listen<NavigationRequestEvent>(NAVIGATION_REQUESTED_EVENT, event => handler(event.payload))
  }

  /** Fires when the webview renderer process terminates abnormally. */
  onRenderProcessGone(handler: (event: RenderProcessGoneEvent) => void): Unlisten {
    return listen<RenderProcessGoneEvent>(RENDER_PROCESS_GONE_EVENT, event => handler(event.payload))
  }

  private target(): Record<string, unknown> {
    return this.label === undefined ? {} : { label: this.label }
  }
}

/** Shorthand for {@link Webview.getCurrent}. */
export function getCurrentWebview(): Webview {
  return Webview.getCurrent()
}