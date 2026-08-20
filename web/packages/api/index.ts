/**
 * @tarui/api — typed frontend bindings for the tarui.net shell.
 *
 * Every module mirrors one native plugin contract:
 * - ipc       : raw invoke/listen bridge (rarely needed directly)
 * - app       : shell handshake
 * - window    : window lifecycle, state, and events
 * - event     : cross-window event emit/subscribe
 * - dialog    : native file pickers
 * - os        : host operating system info
 * - path      : well-known directory resolution
 * - process   : exit and relaunch
 * - shell     : OS default handler for URLs and files
 * - clipboard : system clipboard text access
 *
 * `dialog.open` and `shell.open` are re-exported as `openDialog` and
 * `openExternal` here to keep the barrel collision-free; the subpath
 * exports keep their Tauri-style short names.
 */

export { invoke, listen, Channel, IpcCommandError } from './ipc'
export type { Unlisten } from './ipc'

export { getAppInfo } from './app'
export type { AppInfo } from './app'

export {
  Window,
  getCurrentWindow,
  WINDOW_MOVED_EVENT,
  WINDOW_RESIZED_EVENT,
  WINDOW_FOCUS_CHANGED_EVENT,
  WINDOW_CLOSE_REQUESTED_EVENT,
  WINDOW_DESTROYED_EVENT,
} from './window'
export type {
  LogicalPosition,
  LogicalSize,
  Monitor,
  WindowState,
  WindowOptions,
  WindowGeometry,
} from './window'

export { emit, on } from './event'

export { open as openDialog, save as saveDialog } from './dialog'
export type { OpenDialogOptions, SaveDialogOptions } from './dialog'

export { info as getOsInfo } from './os'
export type { OsInfo } from './os'

export { resolve as resolvePath } from './path'
export type { PathKind } from './path'

export { exit, relaunch } from './process'

export { open as openExternal } from './shell'
export type { ShellOpenResult } from './shell'

export { readText, writeText } from './clipboard'
