import { invoke, listen } from './ipc'
import type { Unlisten } from './ipc'

export type LogicalPosition = { x: number; y: number }
export type LogicalSize = { width: number; height: number }

export type Monitor = {
  name: string
  position: LogicalPosition
  size: LogicalSize
  workAreaPosition: LogicalPosition
  workAreaSize: LogicalSize
  scaleFactor: number
  isPrimary: boolean
  isCurrent: boolean
}

export type WindowState = {
  label: string
  title: string
  isFocused: boolean
  isFullscreen: boolean
  isMaximized: boolean
  isMinimized: boolean
  isVisible: boolean
  isDecorated: boolean
  isResizable: boolean
  isAlwaysOnTop: boolean
  theme: string
  scaleFactor: number
  position: LogicalPosition
  size: LogicalSize
}

export type WindowOptions = {
  label: string
  url?: string
  title?: string
  width?: number
  height?: number
  minWidth?: number
  minHeight?: number
  maxWidth?: number
  maxHeight?: number
  x?: number
  y?: number
  center?: boolean
  resizable?: boolean
  decorations?: boolean
  alwaysOnTop?: boolean
  visible?: boolean
}

export type WindowGeometry = {
  x: number
  y: number
  width: number
  height: number
}

type WindowFocusChanged = { focused: boolean }
type WindowLabelPayload = { label: string }

export const WINDOW_MOVED_EVENT = 'window://moved'
export const WINDOW_RESIZED_EVENT = 'window://resized'
export const WINDOW_FOCUS_CHANGED_EVENT = 'window://focus-changed'
export const WINDOW_CLOSE_REQUESTED_EVENT = 'window://close-requested'
export const WINDOW_DESTROYED_EVENT = 'window://destroyed'

export const FILE_DROP_ENTERED_EVENT = 'window://file-drop-entered'
export const FILE_DROP_LEFT_EVENT = 'window://file-drop-left'
export const FILE_DROPPED_EVENT = 'window://file-dropped'
export const DOWNLOAD_REQUESTED_EVENT = 'webview://download-requested'
export const NAVIGATION_REQUESTED_EVENT = 'webview://navigation-requested'

export type FileDropEvent = {
  /** Absolute paths of the dragged files. */
  paths: string[]
  /** Raw dropped text, when the drop payload contains non-file content. */
  text: string | null
  /** Drop position (CSS pixels) relative to the webview viewport origin. */
  x: number
  y: number
}

export type DownloadRequestEvent = {
  url: string
  suggestedFilename: string | null
}

export type NavigationRequestEvent = {
  url: string
  isMainFrame: boolean
}

/**
 * Typed handle for a shell window. Instances with no label target the
 * window that hosts the calling webview; the shell resolves the label
 * from the request context, so label-less calls always act on the
 * current window even in multi-window apps.
 */
export class Window {
  readonly label: string | undefined

  private constructor(label: string | undefined) {
    this.label = label
  }

  /** Handle for the window hosting the calling webview. */
  static getCurrent(): Window {
    return new Window(undefined)
  }

  /** Handle for a window with a known label. */
  static getByLabel(label: string): Window {
    return new Window(label)
  }

  /** Lists the labels of all live windows and returns handles for them. */
  static async getAll(): Promise<Window[]> {
    const result = await invoke<{ labels: string[] }>('core:window|list', {})
    return result.labels.map(label => Window.getByLabel(label))
  }

  /** Creates a new shell window hosting the same web origin. */
  static async create(options: WindowOptions): Promise<Window> {
    await invoke('core:window|create', options)
    return Window.getByLabel(options.label)
  }

  async close(force = true): Promise<void> {
    await invoke('core:window|close', { force, ...this.target() })
  }

  async minimize(): Promise<void> {
    await invoke('core:window|minimize', this.target())
  }

  async maximize(): Promise<void> {
    await invoke('core:window|maximize', this.target())
  }

  async unmaximize(): Promise<void> {
    await invoke('core:window|unmaximize', this.target())
  }

  async toggleMaximize(): Promise<void> {
    await invoke('core:window|toggle-maximize', this.target())
  }

  async hide(): Promise<void> {
    await invoke('core:window|hide', this.target())
  }

  async show(): Promise<void> {
    await invoke('core:window|show', this.target())
  }

  async focus(): Promise<void> {
    await invoke('core:window|focus', this.target())
  }

  async center(): Promise<void> {
    await invoke('core:window|center', this.target())
  }

  async setTitle(title: string): Promise<void> {
    await invoke('core:window|set-title', { title, ...this.target() })
  }

  async setSize(width: number, height: number): Promise<void> {
    await invoke('core:window|set-size', { width, height, ...this.target() })
  }

  async setPosition(x: number, y: number): Promise<void> {
    await invoke('core:window|set-position', { x, y, ...this.target() })
  }

  async setMinSize(width: number | null, height: number | null): Promise<void> {
    await invoke('core:window|set-min-size', { width, height, ...this.target() })
  }

  async setMaxSize(width: number | null, height: number | null): Promise<void> {
    await invoke('core:window|set-max-size', { width, height, ...this.target() })
  }

  async setAlwaysOnTop(value: boolean): Promise<void> {
    await invoke('core:window|set-always-on-top', { value, ...this.target() })
  }

  async setResizable(value: boolean): Promise<void> {
    await invoke('core:window|set-resizable', { value, ...this.target() })
  }

  async setDecorations(value: boolean): Promise<void> {
    await invoke('core:window|set-decorations', { value, ...this.target() })
  }

  async setFullscreen(value: boolean): Promise<void> {
    await invoke('core:window|set-fullscreen', { value, ...this.target() })
  }

  async getState(): Promise<WindowState> {
    return invoke<WindowState>('core:window|get-state', this.target())
  }

  async currentMonitor(): Promise<Monitor | null> {
    return invoke<Monitor | null>('core:window|current-monitor', this.target())
  }

  async primaryMonitor(): Promise<Monitor | null> {
    return invoke<Monitor | null>('core:window|primary-monitor', this.target())
  }

  async monitors(): Promise<Monitor[]> {
    return invoke<Monitor[]>('core:window|monitors', this.target())
  }

  onFocusChanged(handler: (focused: boolean) => void): Unlisten {
    return listen<WindowFocusChanged>(WINDOW_FOCUS_CHANGED_EVENT, event =>
      handler(event.payload.focused),
    )
  }

  onMoved(handler: (geometry: WindowGeometry) => void): Unlisten {
    return listen<WindowGeometry>(WINDOW_MOVED_EVENT, event => handler(event.payload))
  }

  onResized(handler: (geometry: WindowGeometry) => void): Unlisten {
    return listen<WindowGeometry>(WINDOW_RESIZED_EVENT, event => handler(event.payload))
  }

  onCloseRequested(handler: () => void): Unlisten {
    return listen<WindowLabelPayload>(WINDOW_CLOSE_REQUESTED_EVENT, () => handler())
  }

  onDestroyed(handler: () => void): Unlisten {
    return listen<WindowLabelPayload>(WINDOW_DESTROYED_EVENT, event => {
      if (this.label === undefined || event.payload.label === this.label) {
        handler()
      }
    })
  }

  /** Fires when a file drag enters the webview. The drag is only delivered to windows authorized to receive file paths. */
  onFileDropEntered(handler: (drop: FileDropEvent) => void): Unlisten {
    return listen<FileDropEvent>(FILE_DROP_ENTERED_EVENT, event => handler(event.payload))
  }

  /** Fires when a file drag leaves the webview without being dropped. */
  onFileDropLeft(handler: () => void): Unlisten {
    return listen<WindowLabelPayload>(FILE_DROP_LEFT_EVENT, () => handler())
  }

  /** Fires with the resolved absolute file paths when files are dropped onto the webview. */
  onFileDropped(handler: (drop: FileDropEvent) => void): Unlisten {
    return listen<FileDropEvent>(FILE_DROPPED_EVENT, event => handler(event.payload))
  }

  /** Fires for download requests allowed by the host policy and authorized for this window. */
  onDownloadRequested(handler: (request: DownloadRequestEvent) => void): Unlisten {
    return listen<DownloadRequestEvent>(DOWNLOAD_REQUESTED_EVENT, event => handler(event.payload))
  }

  /** Fires for navigations allowed by the host policy and authorized for this window. */
  onNavigationRequested(handler: (request: NavigationRequestEvent) => void): Unlisten {
    return listen<NavigationRequestEvent>(NAVIGATION_REQUESTED_EVENT, event => handler(event.payload))
  }

  private target(): Record<string, unknown> {
    return this.label === undefined ? {} : { label: this.label }
  }
}

/** Shorthand for {@link Window.getCurrent}. */
export function getCurrentWindow(): Window {
  return Window.getCurrent()
}
