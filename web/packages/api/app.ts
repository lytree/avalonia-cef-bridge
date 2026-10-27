import { invoke } from './ipc'

export type AppInfo = {
  product: string
  shellVersion: string
  bridgeVersion: number
  platform: string
  capabilities: string[]
}

/** Handshake payload describing the shell build and granted capabilities. */
export async function getAppInfo(): Promise<AppInfo> {
  return invoke<AppInfo>('core:app|get-info', {})
}

/**
 * Stops the application through the host's cooperative shutdown, releasing desktop resources in
 * order. Mirrors Tauri 2.12's `app.exit`, which no longer requires the process plugin.
 * @param code Exit code reported to the operating system. Defaults to 0.
 */
export async function exit(code = 0): Promise<void> {
  await invoke('core:app|exit', { code })
}
