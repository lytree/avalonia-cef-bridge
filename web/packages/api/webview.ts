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

/**
 * The identity of the browser runtime backing every webview: the runtime name,
 * the underlying engine version, the asset resource mode and the application
 * origin.
 */
export type WebviewRuntimeInfo = {
  name: string
  webviewVersion: string
  resourceMode: 'http' | 'scheme'
  origin: string
}

/** Fired when the webview renderer process terminates abnormally. */
export const RENDER_PROCESS_GONE_EVENT = 'webview://render-process-gone'

/** Fired for every CDP message (method result or event) the browser emits. */
export const DEVTOOLS_PROTOCOL_EVENT = 'webview://devtools-protocol'

/** A raw Chrome DevTools Protocol JSON message emitted by the browser. */
export type DevtoolsProtocolMessage = {
  message: string
}

/** Position and size of a web view surface inside its host window (logical units). */
export type WebviewBounds = {
  x: number
  y: number
  width: number
  height: number
}

/** Options for creating an additional web view inside an existing window. */
export type WebviewCreateOptions = {
  /** The new web view's label; must be unique among live web views. */
  label: string
  /** The host window label; defaults to the calling window. */
  windowLabel?: string
  /** The start URL; defaults to the application origin. */
  url?: string
  /** The surface bounds; omitted fills the host window. */
  bounds?: WebviewBounds
}

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

  /**
   * Creates an additional web view inside an existing window and returns a handle for it.
   * A surface with bounds is positioned within the window; without bounds it fills the window.
   */
  static async create(options: WebviewCreateOptions): Promise<Webview> {
    const state = await invoke<WebviewState>('plugin:webview|create', options)
    return Webview.getByLabel(state.label)
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

  /** Positions and sizes this webview's surface within its host window; null fills the window. */
  async setBounds(bounds: WebviewBounds | null): Promise<void> {
    await invoke('plugin:webview|set-bounds', { bounds, ...this.target() })
  }

  /** Destroys this webview, releasing its surface and session. */
  async destroy(): Promise<void> {
    await invoke('plugin:webview|destroy', this.target())
  }

  /**
   * Sends a raw Chrome DevTools Protocol message to this webview (for example
   * `{"id":1,"method":"Runtime.enable"}`). Requires the `plugin:webview|send-devtools-message`
   * permission and a runtime that exposes the DevTools protocol channel.
   */
  async sendDevtoolsMessage(message: string): Promise<void> {
    await invoke('plugin:webview|send-devtools-message', { message, ...this.target() })
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

  /** Fires for every CDP protocol message the browser emits for this webview. */
  onDevtoolsProtocol(handler: (message: DevtoolsProtocolMessage) => void): Unlisten {
    return listen<DevtoolsProtocolMessage>(DEVTOOLS_PROTOCOL_EVENT, event => handler(event.payload))
  }

  private target(): Record<string, unknown> {
    return this.label === undefined ? {} : { label: this.label }
  }
}

/** Shorthand for {@link Webview.getCurrent}. */
export function getCurrentWebview(): Webview {
  return Webview.getCurrent()
}

/** Returns the identity of the browser runtime backing every webview. */
export async function runtimeInfo(): Promise<WebviewRuntimeInfo> {
  return invoke<WebviewRuntimeInfo>('plugin:webview|runtime-info', {})
}