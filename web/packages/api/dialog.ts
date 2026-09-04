import { invoke } from './ipc'

export type OpenDialogOptions = {
  multiple?: boolean
  directory?: boolean
  extensions?: string[]
}

export type SaveDialogOptions = {
  defaultName?: string
  extensions?: string[]
}

export type MessageBoxIcon = 'none' | 'info' | 'warning' | 'error' | 'question' | 'success'

export type MessageBoxButton = 'ok' | 'okCancel' | 'yesNo' | 'yesNoCancel'

export type MessageBoxResult = 'ok' | 'cancel' | 'yes' | 'no'

export type MessageBoxOptions = {
  title?: string
  content?: string
  icon?: MessageBoxIcon
  button?: MessageBoxButton
}

export type ConfirmOptions = {
  title?: string
  content?: string
  icon?: MessageBoxIcon
  okLabel?: string
  cancelLabel?: string
}

/**
 * Opens a native file or directory picker attached to the calling
 * window. Returns the selected paths, or an empty array when the
 * dialog is cancelled.
 */
export async function open(options: OpenDialogOptions = {}): Promise<string[]> {
  const result = await invoke<{ paths: string[] }>('plugin:dialog|open', options)
  return result.paths
}

/**
 * Opens a native save dialog attached to the calling window. Returns
 * the chosen path, or null when the dialog is cancelled.
 */
export async function save(options: SaveDialogOptions = {}): Promise<string | null> {
  const result = await invoke<{ path: string | null }>('plugin:dialog|save', options)
  return result.path
}

/**
 * Shows a native message box attached to the calling window. Returns
 * the button the user pressed, or "cancel" when the dialog is dismissed
 * through the window close button.
 */
export async function message(options: MessageBoxOptions = {}): Promise<MessageBoxResult> {
  const result = await invoke<{ result: MessageBoxResult }>('plugin:dialog|message', options)
  return result.result
}

/**
 * Shows a native confirmation dialog attached to the calling window.
 * Returns true when the user confirms, false on cancel or close.
 */
export async function confirm(options: ConfirmOptions = {}): Promise<boolean> {
  const result = await invoke<{ confirmed: boolean }>('plugin:dialog|confirm', options)
  return result.confirmed
}
