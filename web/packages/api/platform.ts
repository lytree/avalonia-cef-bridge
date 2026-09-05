import { invoke } from './ipc'

/** Runtime availability of OS-coupled features, reported by `core:platform|capabilities`. */
export interface PlatformCapabilities {
  notificationSupported: boolean
  notificationReason?: string | null
  globalShortcutSupported: boolean
  globalShortcutReason?: string | null
  autostartSupported: boolean
  deepLinkSupported: boolean
  deepLinkReason?: string | null
}

/**
 * Returns which OS-coupled features are genuinely implemented on the running platform, so UI can disable or
 * hide what would otherwise hit an honest runtime degraded no-op. Requires `core:platform|capabilities`.
 */
export async function capabilities(): Promise<PlatformCapabilities> {
  return invoke<PlatformCapabilities>('core:platform|capabilities')
}

export const platform = { capabilities } as const