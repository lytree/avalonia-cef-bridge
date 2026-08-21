import { invoke } from './ipc'

/** Options for `plugin:autostart|enable`. */
export interface AutostartEnableOptions {
  /** Fixed pre-configured arguments forwarded when the process launches at login. */
  args?: string[]
}

/** Result of `plugin:autostart|is-enabled`. */
export interface AutostartState {
  enabled: boolean
}

/** Queries whether the current application is registered to launch at login. */
export async function isEnabled(): Promise<AutostartState> {
  return invoke('plugin:autostart|is-enabled')
}

/** Registers the current application to launch at login. */
export async function enable(options: AutostartEnableOptions = {}): Promise<void> {
  await invoke('plugin:autostart|enable', options)
}

/** Removes the current application from the login launch list. */
export async function disable(): Promise<void> {
  await invoke('plugin:autostart|disable')
}

export const autostart = {
  isEnabled,
  enable,
  disable,
} as const