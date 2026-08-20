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
