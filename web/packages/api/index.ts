/**
 * @lytree/api — typed frontend bindings for the tarui.net shell.
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
 * - fs        : scoped restricted file system operations
 * - menu      : native window menu
 * - tray      : system tray icon and menu
 * - window-state : window geometry and state persistence
 * - single-instance : second-instance activation events
 * - notification    : system notification permission, show/cancel and events
 * - autostart       : launch-at-login registration
 * - global-shortcut : system-wide shortcut registration and trigger events
 * - store           : lightweight JSON configuration persistence
 * - log             : renderer log forwarding and desktop log event subscription
 * - deep-link       : current launch URL and custom-protocol activation events
 * - updater         : signed update check and download staging
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
  FILE_DROP_ENTERED_EVENT,
  FILE_DROP_LEFT_EVENT,
  FILE_DROPPED_EVENT,
  DOWNLOAD_REQUESTED_EVENT,
  NAVIGATION_REQUESTED_EVENT,
} from './window'
export type {
  LogicalPosition,
  LogicalSize,
  Monitor,
  WindowState,
  WindowOptions,
  WindowGeometry,
  FileDropEvent,
  DownloadRequestEvent,
  NavigationRequestEvent,
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

export {
  fs,
  readTextFile,
  writeTextFile,
  readDir,
  stat,
  exists,
  mkdir,
  copyFile,
  rename,
  fsBaseIdentifiers,
} from './fs'
export type {
  FsBaseId,
  FsPathOptions,
  FsReadDirOptions,
  FsWriteTextOptions,
  FsCopyOptions,
  FsRenameOptions,
  FsMkdirOptions,
  FsRemoveOptions,
  FsDirEntry,
  FsStatResult,
  FsReadTextResult,
} from './fs'

export { menu, setWindowMenu, updateItem, removeWindowMenu, menuItemKinds } from './menu'
export type { MenuItemDefinition, MenuItemKind, SetWindowMenuOptions, MenuUpdateItemOptions, MenuItemClicked } from './menu'

export { tray, create as createTray, setMenu as setTrayMenu, setIcon as setTrayIcon, setTooltip as setTrayTooltip, setVisible as setTrayVisible, remove as removeTray } from './tray'
export type { TrayCreateOptions, TraySetMenuOptions, TraySetIconOptions, TraySetTooltipOptions, TraySetVisibleOptions, TrayRemoveOptions, TrayClicked, TrayMenuItemClicked } from './tray'

export { windowState, save as saveWindowState, restore as restoreWindowState, clear as clearWindowState } from './window-state'
export type { WindowStateOptions, WindowStateSnapshot, WindowStateRestoreResult } from './window-state'

export { onSecondInstance, singleInstance, SECOND_INSTANCE_EVENT } from './single-instance'
export type { SecondInstanceArgs } from './single-instance'

export {
  notification,
  permissionState,
  requestPermission,
  show as showNotification,
  cancel as cancelNotification,
  onNotificationActivated,
  onNotificationDismissed,
  notificationPermissionState,
  NOTIFICATION_ACTIVATED_EVENT,
  NOTIFICATION_DISMISSED_EVENT,
} from './notification'
export type { NotificationOptions, NotificationPermissionStateResult, NotificationEvent, NotificationPermission } from './notification'

export { autostart, isEnabled as isAutostartEnabled, enable as enableAutostart, disable as disableAutostart } from './autostart'
export type { AutostartEnableOptions, AutostartState } from './autostart'

export {
  globalShortcut,
  register as registerGlobalShortcut,
  unregister as unregisterGlobalShortcut,
  unregisterAll as unregisterAllGlobalShortcuts,
  isRegistered as isGlobalShortcutRegistered,
  onGlobalShortcut,
  GLOBAL_SHORTCUT_TRIGGERED_EVENT,
} from './global-shortcut'
export type { GlobalShortcutOptions, GlobalShortcutState, GlobalShortcutTriggered } from './global-shortcut'

export {
  store,
  get as getStore,
  set as setStore,
  has as storeHas,
  remove as removeStore,
  clear as clearStore,
  keys as storeKeys,
  storeBaseIdentifiers,
} from './store'
export type { StoreBaseId, StoreFileOptions, StoreKeyOptions, StoreSetOptions, StoreGetResult, StoreHasResult, StoreKeysResult } from './store'

export {
  log,
  record as recordLog,
  trace as logTrace,
  debug as logDebug,
  info as logInfo,
  warn as logWarn,
  error as logError,
  critical as logCritical,
  onLogEntry,
  logLevels,
  LOG_ENTRY_EVENT,
} from './log'
export type { LogLevel, LogRecordOptions, LogEntry } from './log'

export { deepLink, getCurrent as getCurrentDeepLink, feed as feedDeepLink, onDeepLink } from './deep-link'
export type { DeepLinkCurrentResult, DeepLinkFeedOptions } from './deep-link'

export {
  updater,
  check as checkUpdate,
  download as downloadUpdate,
  onStatus as onUpdaterStatus,
  updaterStatusPhases,
  UPDATER_STATUS_EVENT,
} from './updater'
export type { UpdateCheckResult, UpdateDownloadResult, UpdaterStatus, UpdaterStatusPhase } from './updater'
