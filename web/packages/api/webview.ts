import { invoke, listen } from './ipc'
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

  /** Fires for download requests allowed by the host policy and authorized for this webview. */
  onDownloadRequested(handler: (request: DownloadRequestEvent) => void): Unlisten {
    return listen<DownloadRequestEvent>(DOWNLOAD_REQUESTED_EVENT, event => handler(event.payload))
  }

  /** Fires for navigations allowed by the host policy and authorized for this webview. */
  onNavigationRequested(handler: (request: NavigationRequestEvent) => void): Unlisten {
    return listen<NavigationRequestEvent>(NAVIGATION_REQUESTED_EVENT, event => handler(event.payload))
  }

  private target(): Record<string, unknown> {
    return this.label === undefined ? {} : { label: this.label }
  }
}

/** Shorthand for {@link Webview.getCurrent}. */
export function getCurrentWebview(): Webview {
  return Webview.getCurrent()
}