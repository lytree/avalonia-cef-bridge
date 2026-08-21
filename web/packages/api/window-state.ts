import { invoke } from './ipc'

/** Targets a window for window-state operations; defaults to the calling window. */
export interface WindowStateOptions {
  label?: string | undefined
}

/** Persisted window geometry and state (device-independent pixels, unless maximized/fullscreen). */
export interface WindowStateSnapshot {
  label: string
  x: number
  y: number
  width: number
  height: number
  isMaximized?: boolean
  isFullscreen?: boolean
}

/** Result of `plugin:window-state|restore`. */
export interface WindowStateRestoreResult {
  /** False when no saved state was found for the target window. */
  applied: boolean
}

/** Persists the window's current geometry and state. */
export function save(options: WindowStateOptions = {}): Promise<void> {
  return invoke('plugin:window-state|save', options)
}

/** Restores the window from persisted state, clamped to the current monitor set. */
export function restore(options: WindowStateOptions = {}): Promise<WindowStateRestoreResult> {
  return invoke('plugin:window-state|restore', options)
}

/** Removes the window's persisted state. */
export function clear(options: WindowStateOptions = {}): Promise<void> {
  return invoke('plugin:window-state|clear', options)
}

export const windowState = {
  save,
  restore,
  clear,
} as const