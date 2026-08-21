import { invoke, listen } from './ipc'
import type { Unlisten } from './ipc'

/** Options for `plugin:global-shortcut|register`. */
export interface GlobalShortcutOptions {
  /** A normalized shortcut like `Ctrl+Shift+A`. */
  accelerator: string
}

/** Result of `plugin:global-shortcut|register`. */
export interface GlobalShortcutState {
  registered: boolean
}

/** Payload of `global-shortcut://triggered`. */
export interface GlobalShortcutTriggered {
  accelerator: string
}

/** Event delivered to authorized windows when a registered global shortcut fires. */
export const GLOBAL_SHORTCUT_TRIGGERED_EVENT = 'global-shortcut://triggered'

/** Registers a process-wide shortcut; requires the accelerator to be within capability scope. */
export async function register(accelerator: string): Promise<GlobalShortcutState> {
  return invoke('plugin:global-shortcut|register', { accelerator })
}

/** Unregisters a previously registered shortcut. */
export async function unregister(accelerator: string): Promise<void> {
  await invoke('plugin:global-shortcut|unregister', { accelerator })
}

/** Unregisters every shortcut registered by this application. */
export async function unregisterAll(): Promise<void> {
  await invoke('plugin:global-shortcut|unregister-all')
}

/** Queries whether a shortcut is currently registered. */
export async function isRegistered(accelerator: string): Promise<GlobalShortcutState> {
  return invoke('plugin:global-shortcut|is-registered', { accelerator })
}

/** Subscribes to global shortcut trigger events (requires `global-shortcut://triggered` capability). */
export function onGlobalShortcut(callback: (event: GlobalShortcutTriggered) => void): Unlisten {
  return listen<GlobalShortcutTriggered>(GLOBAL_SHORTCUT_TRIGGERED_EVENT, received => callback(received.payload))
}

export const globalShortcut = {
  register,
  unregister,
  unregisterAll,
  isRegistered,
  onGlobalShortcut,
} as const